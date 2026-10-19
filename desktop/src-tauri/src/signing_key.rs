//! The JWT signing key for participant tokens.
//!
//! LAN and internet players authenticate with a token the backend signs with `Jwt:SigningKey`.
//! Without a key the backend cannot issue or validate tokens at all, so a LAN player's join fails.
//! The sidecar therefore supplies one, as the `Jwt__SigningKey` environment variable.
//!
//! **Persisted, not per-launch.** The key is generated once from the operating system's random
//! source and kept in a file in the app data directory, so tokens survive a restart of the app.
//! A key generated at every launch would invalidate every token the moment the host restarted (or
//! crashed, or auto-updated): each player would be rejected on reconnect, have to rejoin with the
//! invite code as a *new* participant, and be approved and given their character again, while the
//! old participant row stayed behind (and in a group adventure kept its place in the turn order).
//!
//! **Handling.** The key is never logged and never written anywhere but the key file. [`SigningKey`]
//! prints as `SigningKey(<redacted>)` so a stray `{:?}` cannot leak it. The file is created with
//! owner-only permissions on Unix; on Windows it inherits the user-only access of the per-user
//! app data directory. Anyone who can read it can already read the host's AI settings and database
//! credentials. Deleting the file rotates the key (and invalidates every outstanding token).
//!
//! The randomness is injected so the logic is testable without the OS; the sidecar passes `getrandom`.

use std::fmt;
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};

/// Name of the key file inside the app data directory.
pub const KEY_FILE_NAME: &str = "jwt-signing-key";

/// Random bytes in a key: 256 bits, the strength HMAC-SHA256 needs.
const KEY_BYTES: usize = 32;

/// Length of the hex encoding. The backend requires at least 32 characters; this is 64.
const KEY_HEX_LEN: usize = KEY_BYTES * 2;

/// A signing key. Deliberately not `Display`, and `Debug` redacts the value.
#[derive(Clone, PartialEq, Eq)]
pub struct SigningKey(String);

impl SigningKey {
    /// The key text, for handing to the backend process. Do not log it.
    pub fn expose(&self) -> &str {
        &self.0
    }

    /// True for exactly the format this module writes: 64 lowercase hex characters.
    fn is_valid(text: &str) -> bool {
        text.len() == KEY_HEX_LEN && text.bytes().all(|b| matches!(b, b'0'..=b'9' | b'a'..=b'f'))
    }
}

impl fmt::Debug for SigningKey {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("SigningKey(<redacted>)")
    }
}

/// Where the key the backend runs with came from. Never contains the key.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum KeySource {
    /// The environment the app was started from already had `Jwt__SigningKey`; it is left alone.
    Environment,
    /// Read from the key file.
    LoadedFromFile,
    /// Generated and saved to the key file (the first launch, or the file was missing).
    CreatedFile,
    /// The key file was unreadable or malformed and was replaced with a new key.
    ReplacedFile,
    /// The key file could not be written, so this launch uses a key that lives in memory only.
    /// Tokens will not survive a restart.
    InMemoryOnly,
}

/// The outcome of [`resolve`].
#[derive(Debug)]
pub struct Resolved {
    pub source: KeySource,
    /// The key to pass to the backend, or `None` when the environment already provides one.
    pub key: Option<SigningKey>,
    /// Why the key could not be persisted, when `source` is [`KeySource::InMemoryOnly`]. Contains no key.
    pub warning: Option<String>,
}

/// Fills the buffer with cryptographically secure random bytes, or says why it could not.
pub type FillRandom<'a> = &'a mut dyn FnMut(&mut [u8]) -> Result<(), String>;

/// Decides which signing key the backend runs with.
///
/// 1. `existing_env` (the value of `Jwt__SigningKey` in the app's own environment) wins if it is
///    long enough for the backend (32 characters). It is not copied or touched.
/// 2. Otherwise the persisted key in `data_dir` is used, created on first use.
/// 3. If the file cannot be used at all, a fresh in-memory key is generated for this launch.
///
/// Fails only if the operating system cannot supply randomness, in which case the backend must not
/// be started with a weak or missing key.
pub fn resolve(
    data_dir: Option<&Path>,
    existing_env: Option<&str>,
    fill: FillRandom<'_>,
) -> Result<Resolved, String> {
    if existing_env.is_some_and(|value| value.trim().len() >= 32) {
        return Ok(Resolved { source: KeySource::Environment, key: None, warning: None });
    }

    let Some(dir) = data_dir else {
        let key = generate(fill)?;
        return Ok(Resolved {
            source: KeySource::InMemoryOnly,
            key: Some(key),
            warning: Some("no app data directory is available".to_string()),
        });
    };

    match load_or_create(dir, fill) {
        Ok((key, source)) => Ok(Resolved { source, key: Some(key), warning: None }),
        Err(LoadError::Random(message)) => Err(message),
        Err(LoadError::Io(message)) => {
            // Keep the app usable; only reconnects after a restart are lost.
            let key = generate(fill)?;
            Ok(Resolved { source: KeySource::InMemoryOnly, key: Some(key), warning: Some(message) })
        }
    }
}

/// Why [`load_or_create`] failed.
#[derive(Debug)]
pub enum LoadError {
    /// The key file could not be read or written.
    Io(String),
    /// The operating system's random source failed.
    Random(String),
}

/// Reads the key file in `dir`, or creates it. A file that is not exactly a key this module wrote
/// (empty, truncated, hand-edited) is replaced rather than trusted.
pub fn load_or_create(dir: &Path, fill: FillRandom<'_>) -> Result<(SigningKey, KeySource), LoadError> {
    let path = dir.join(KEY_FILE_NAME);

    let mut replaced = false;
    match fs::read_to_string(&path) {
        Ok(text) if SigningKey::is_valid(text.trim()) => {
            restrict_permissions(&path);
            return Ok((SigningKey(text.trim().to_string()), KeySource::LoadedFromFile));
        }
        Ok(_) => replaced = true,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
        // Unreadable (permissions, a directory in the way): do not guess, let the caller fall back.
        Err(e) => return Err(LoadError::Io(format!("could not read the signing key file: {e}"))),
    }

    let key = generate(fill).map_err(LoadError::Random)?;
    fs::create_dir_all(dir).map_err(|e| LoadError::Io(format!("could not create the app data directory: {e}")))?;
    write_atomically(&path, key.expose())
        .map_err(|e| LoadError::Io(format!("could not save the signing key file: {e}")))?;
    Ok((key, if replaced { KeySource::ReplacedFile } else { KeySource::CreatedFile }))
}

/// A new random key: 32 random bytes as 64 lowercase hex characters.
pub fn generate(fill: FillRandom<'_>) -> Result<SigningKey, String> {
    let mut bytes = [0u8; KEY_BYTES];
    fill(&mut bytes).map_err(|e| format!("could not get random bytes for the signing key: {e}"))?;

    let mut hex = String::with_capacity(KEY_HEX_LEN);
    for byte in bytes {
        hex.push(char::from_digit(u32::from(byte >> 4), 16).expect("a nibble is a hex digit"));
        hex.push(char::from_digit(u32::from(byte & 0x0f), 16).expect("a nibble is a hex digit"));
    }
    Ok(SigningKey(hex))
}

/// Writes `contents` to `path` through a temporary file and a rename, so a crash never leaves a
/// half-written key. The temporary file is created owner-only on Unix.
fn write_atomically(path: &Path, contents: &str) -> std::io::Result<()> {
    let tmp: PathBuf = path.with_extension("tmp");
    // A previous run may have crashed after creating it.
    let _ = fs::remove_file(&tmp);

    let mut options = fs::OpenOptions::new();
    options.write(true).create_new(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }

    let result = (|| {
        let mut file = options.open(&tmp)?;
        file.write_all(contents.as_bytes())?;
        file.sync_all()?;
        drop(file);
        fs::rename(&tmp, path)
    })();

    if result.is_err() {
        let _ = fs::remove_file(&tmp);
    }
    result
}

/// Best effort: make an existing key file owner-only on Unix. A no-op elsewhere.
fn restrict_permissions(path: &Path) {
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let _ = fs::set_permissions(path, fs::Permissions::from_mode(0o600));
    }
    #[cfg(not(unix))]
    {
        let _ = path;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};

    static COUNTER: AtomicUsize = AtomicUsize::new(0);

    /// A scratch directory removed on drop.
    struct TempDir(PathBuf);

    impl TempDir {
        fn new() -> Self {
            let n = COUNTER.fetch_add(1, Ordering::SeqCst);
            let path = std::env::temp_dir().join(format!("aircane-signing-key-{}-{n}", std::process::id()));
            let _ = fs::remove_dir_all(&path);
            TempDir(path)
        }
    }

    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    /// Bytes 0, 1, 2, ... so the hex encoding is predictable.
    fn counting(buf: &mut [u8]) -> Result<(), String> {
        for (i, b) in buf.iter_mut().enumerate() {
            *b = i as u8;
        }
        Ok(())
    }

    /// Bytes derived from a seed, so two keys differ.
    fn seeded(seed: u8) -> impl FnMut(&mut [u8]) -> Result<(), String> {
        move |buf| {
            for (i, b) in buf.iter_mut().enumerate() {
                *b = seed.wrapping_add(i as u8).wrapping_mul(7);
            }
            Ok(())
        }
    }

    fn failing(buf: &mut [u8]) -> Result<(), String> {
        let _ = buf;
        Err("no entropy".to_string())
    }

    fn must_not_be_called(_: &mut [u8]) -> Result<(), String> {
        panic!("the random source must not be used when a valid key already exists");
    }

    const COUNTING_KEY: &str = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";

    #[test]
    fn generate_is_32_bytes_as_64_lowercase_hex() {
        let key = generate(&mut counting).unwrap();

        assert_eq!(key.expose(), COUNTING_KEY);
        assert!(SigningKey::is_valid(key.expose()));
    }

    #[test]
    fn generate_reports_a_random_source_failure() {
        let error = generate(&mut failing).unwrap_err();

        assert!(error.contains("no entropy"), "{error}");
    }

    #[test]
    fn debug_never_shows_the_key() {
        let key = generate(&mut counting).unwrap();

        let shown = format!("{key:?}");

        assert_eq!(shown, "SigningKey(<redacted>)");
        assert!(!shown.contains("0102"));
    }

    #[test]
    fn first_launch_creates_the_file() {
        let dir = TempDir::new();

        let (key, source) = load_or_create(&dir.0, &mut counting).unwrap();

        assert_eq!(source, KeySource::CreatedFile);
        assert_eq!(fs::read_to_string(dir.0.join(KEY_FILE_NAME)).unwrap(), key.expose());
    }

    #[test]
    fn second_launch_reuses_the_same_key_without_touching_the_random_source() {
        let dir = TempDir::new();
        let (first, _) = load_or_create(&dir.0, &mut counting).unwrap();

        let (second, source) = load_or_create(&dir.0, &mut must_not_be_called).unwrap();

        assert_eq!(source, KeySource::LoadedFromFile);
        assert_eq!(first, second);
    }

    #[test]
    fn a_trailing_newline_in_the_file_is_tolerated() {
        let dir = TempDir::new();
        fs::create_dir_all(&dir.0).unwrap();
        fs::write(dir.0.join(KEY_FILE_NAME), format!("{COUNTING_KEY}\n")).unwrap();

        let (key, source) = load_or_create(&dir.0, &mut must_not_be_called).unwrap();

        assert_eq!(source, KeySource::LoadedFromFile);
        assert_eq!(key.expose(), COUNTING_KEY);
    }

    #[test]
    fn a_malformed_file_is_replaced_not_trusted() {
        for bad in ["", "short", "not hex not hex not hex not hex not hex not hex not hex not hex!!", &COUNTING_KEY.to_uppercase(), &COUNTING_KEY[..63]] {
            let dir = TempDir::new();
            fs::create_dir_all(&dir.0).unwrap();
            fs::write(dir.0.join(KEY_FILE_NAME), bad).unwrap();

            let (key, source) = load_or_create(&dir.0, &mut counting).unwrap();

            assert_eq!(source, KeySource::ReplacedFile, "for {bad:?}");
            assert_eq!(key.expose(), COUNTING_KEY);
            assert_eq!(fs::read_to_string(dir.0.join(KEY_FILE_NAME)).unwrap(), COUNTING_KEY);
        }
    }

    #[test]
    fn a_missing_directory_is_created() {
        let dir = TempDir::new();
        let nested = dir.0.join("a").join("b");

        let (_, source) = load_or_create(&nested, &mut counting).unwrap();

        assert_eq!(source, KeySource::CreatedFile);
        assert!(nested.join(KEY_FILE_NAME).is_file());
    }

    #[test]
    fn no_temporary_file_is_left_behind() {
        let dir = TempDir::new();
        load_or_create(&dir.0, &mut counting).unwrap();

        let names: Vec<_> = fs::read_dir(&dir.0).unwrap().map(|e| e.unwrap().file_name()).collect();

        assert_eq!(names, [std::ffi::OsString::from(KEY_FILE_NAME)]);
    }

    #[test]
    fn a_stale_temporary_file_from_a_crashed_run_does_not_block_saving() {
        let dir = TempDir::new();
        fs::create_dir_all(&dir.0).unwrap();
        fs::write(dir.0.join("jwt-signing-key.tmp"), "half written").unwrap();

        let (_, source) = load_or_create(&dir.0, &mut counting).unwrap();

        assert_eq!(source, KeySource::CreatedFile);
        assert!(!dir.0.join("jwt-signing-key.tmp").exists());
    }

    #[test]
    fn different_installs_get_different_keys() {
        let (a, b) = (TempDir::new(), TempDir::new());

        let (first, _) = load_or_create(&a.0, &mut seeded(1)).unwrap();
        let (second, _) = load_or_create(&b.0, &mut seeded(2)).unwrap();

        assert_ne!(first, second);
    }

    #[cfg(unix)]
    #[test]
    fn the_file_is_owner_only_on_unix() {
        use std::os::unix::fs::PermissionsExt;
        let dir = TempDir::new();

        load_or_create(&dir.0, &mut counting).unwrap();

        let mode = fs::metadata(dir.0.join(KEY_FILE_NAME)).unwrap().permissions().mode() & 0o777;
        assert_eq!(mode, 0o600);
    }

    #[cfg(unix)]
    #[test]
    fn an_existing_world_readable_file_is_tightened_on_unix() {
        use std::os::unix::fs::PermissionsExt;
        let dir = TempDir::new();
        fs::create_dir_all(&dir.0).unwrap();
        let path = dir.0.join(KEY_FILE_NAME);
        fs::write(&path, COUNTING_KEY).unwrap();
        fs::set_permissions(&path, fs::Permissions::from_mode(0o644)).unwrap();

        load_or_create(&dir.0, &mut must_not_be_called).unwrap();

        assert_eq!(fs::metadata(&path).unwrap().permissions().mode() & 0o777, 0o600);
    }

    // ── resolve ───────────────────────────────────────────────────────────────

    #[test]
    fn an_environment_key_is_left_alone() {
        let dir = TempDir::new();
        let long_enough = "x".repeat(40);

        let resolved = resolve(Some(&dir.0), Some(&long_enough), &mut must_not_be_called).unwrap();

        assert_eq!(resolved.source, KeySource::Environment);
        assert!(resolved.key.is_none());
        assert!(!dir.0.exists(), "nothing should be written when the environment provides the key");
    }

    #[test]
    fn a_too_short_environment_key_is_ignored_because_the_backend_would_reject_it() {
        let dir = TempDir::new();

        let resolved = resolve(Some(&dir.0), Some("short"), &mut counting).unwrap();

        assert_eq!(resolved.source, KeySource::CreatedFile);
        assert_eq!(resolved.key.unwrap().expose(), COUNTING_KEY);
    }

    #[test]
    fn with_no_environment_key_the_persisted_key_is_used() {
        let dir = TempDir::new();
        let first = resolve(Some(&dir.0), None, &mut counting).unwrap();

        let second = resolve(Some(&dir.0), None, &mut must_not_be_called).unwrap();

        assert_eq!(first.source, KeySource::CreatedFile);
        assert_eq!(second.source, KeySource::LoadedFromFile);
        assert_eq!(first.key, second.key);
    }

    #[test]
    fn with_no_data_directory_the_key_lives_in_memory_only() {
        let resolved = resolve(None, None, &mut counting).unwrap();

        assert_eq!(resolved.source, KeySource::InMemoryOnly);
        assert!(resolved.key.is_some());
        assert!(resolved.warning.is_some());
    }

    #[test]
    fn an_unwritable_directory_falls_back_to_an_in_memory_key() {
        let dir = TempDir::new();
        fs::create_dir_all(&dir.0).unwrap();
        // A regular file where the directory should be, so the key file cannot be created under it.
        let blocker = dir.0.join("blocker");
        fs::write(&blocker, "x").unwrap();

        let resolved = resolve(Some(&blocker.join("data")), None, &mut counting).unwrap();

        assert_eq!(resolved.source, KeySource::InMemoryOnly);
        assert_eq!(resolved.key.unwrap().expose(), COUNTING_KEY);
        let warning = resolved.warning.unwrap();
        assert!(!warning.contains(COUNTING_KEY), "the warning must never contain the key");
    }

    #[test]
    fn a_random_source_failure_stops_startup_instead_of_using_a_weak_key() {
        let dir = TempDir::new();

        assert!(resolve(Some(&dir.0), None, &mut failing).is_err());
        assert!(resolve(None, None, &mut failing).is_err());
        assert!(!dir.0.join(KEY_FILE_NAME).exists());
    }
}
