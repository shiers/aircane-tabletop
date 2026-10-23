/**
 * Reading what a failed API call says. A caught error is `unknown`; these helpers look inside it the way the views
 * always did (`error.response.data.<field>`, then `error.message`), with structural checks instead of `any`, so they
 * work for an Axios error and for any other thrown object.
 */

type Body = Record<string, unknown>

function isRecord(value: unknown): value is Body {
  return typeof value === 'object' && value !== null
}

/** The parsed body of the error response, when there is one and it is an object. */
export function apiErrorBody(error: unknown): Body | undefined {
  if (!isRecord(error) || !isRecord(error.response)) return undefined
  return isRecord(error.response.data) ? error.response.data : undefined
}

/** The HTTP status of the error response, when there is one. */
export function apiErrorStatus(error: unknown): number | undefined {
  if (!isRecord(error) || !isRecord(error.response)) return undefined
  return typeof error.response.status === 'number' ? error.response.status : undefined
}

/**
 * The message to show for a failed call: the response body's `field` when the server sent a string there, otherwise
 * the error's own message, otherwise `fallback`.
 */
export function apiErrorMessage(error: unknown, field: 'detail' | 'error' | 'message', fallback: string): string {
  const fromServer = apiErrorBody(error)?.[field]
  if (typeof fromServer === 'string') return fromServer
  if (isRecord(error) && typeof error.message === 'string') return error.message
  return fallback
}

/**
 * The validation problems of a 400 response (`errors`: field name to messages) joined into one line, or undefined
 * when the response has none.
 */
export function apiValidationMessage(error: unknown): string | undefined {
  const errors = apiErrorBody(error)?.errors
  if (!isRecord(errors)) return undefined
  return Object.values(errors).flat().join(' ')
}
