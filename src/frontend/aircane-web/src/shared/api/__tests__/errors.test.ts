import { describe, expect, it } from 'vitest'
import { AxiosError, type AxiosResponse } from 'axios'
import { apiErrorBody, apiErrorMessage, apiErrorStatus, apiValidationMessage } from '../errors'

/** A real Axios error, as the API client rejects with. */
function axiosError(status: number, data: unknown, message = `Request failed with status code ${status}`): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(message, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

/** What a spec rejects with: a plain object, not an Axios error. */
const plain = (status: number, data: unknown) => ({ response: { status, data } })

describe('apiErrorBody and apiErrorStatus', () => {
  it('read the response of an Axios error and of a plain object alike', () => {
    expect(apiErrorBody(axiosError(404, { detail: 'Nope' }))).toEqual({ detail: 'Nope' })
    expect(apiErrorStatus(axiosError(404, {}))).toBe(404)
    expect(apiErrorBody(plain(500, { error: 'Boom' }))).toEqual({ error: 'Boom' })
    expect(apiErrorStatus(plain(500, {}))).toBe(500)
  })

  it.each([undefined, null, 'boom', 42, new Error('no response'), {}, { response: null }, { response: 'x' }])(
    'find nothing in %j',
    (error) => {
      expect(apiErrorBody(error)).toBeUndefined()
      expect(apiErrorStatus(error)).toBeUndefined()
    },
  )

  it('ignore a response body that is not an object (an HTML error page, say)', () => {
    expect(apiErrorBody(plain(502, '<html>Bad gateway</html>'))).toBeUndefined()
    expect(apiErrorStatus(plain(502, '<html>Bad gateway</html>'))).toBe(502)
  })
})

describe('apiErrorMessage', () => {
  it.each(['detail', 'error', 'message'] as const)('prefers the server\'s "%s" over the error message and the fallback', (field) => {
    const error = axiosError(400, { [field]: 'From the server' }, 'Axios says')

    expect(apiErrorMessage(error, field, 'Fallback')).toBe('From the server')
  })

  it('reads only the field it is asked for', () => {
    const error = axiosError(400, { detail: 'The detail' }, 'Axios says')

    expect(apiErrorMessage(error, 'error', 'Fallback')).toBe('Axios says')
  })

  it('falls back to the error\'s own message when the body has no usable field', () => {
    expect(apiErrorMessage(axiosError(500, {}, 'Network Error'), 'detail', 'Fallback')).toBe('Network Error')
    expect(apiErrorMessage(axiosError(500, '<html/>', 'Network Error'), 'detail', 'Fallback')).toBe('Network Error')
    expect(apiErrorMessage(new Error('Plain failure'), 'error', 'Fallback')).toBe('Plain failure')
    expect(apiErrorMessage({ message: 'Spec rejection' }, 'error', 'Fallback')).toBe('Spec rejection')
  })

  it('ignores a field that is not a string', () => {
    expect(apiErrorMessage(axiosError(400, { detail: { nested: true } }, 'Axios says'), 'detail', 'Fallback')).toBe('Axios says')
    expect(apiErrorMessage(plain(400, { detail: 42 }), 'detail', 'Fallback')).toBe('Fallback')
  })

  it('uses the fallback when there is nothing to read', () => {
    expect(apiErrorMessage(undefined, 'detail', 'Fallback')).toBe('Fallback')
    expect(apiErrorMessage('boom', 'detail', 'Fallback')).toBe('Fallback')
    expect(apiErrorMessage({}, 'detail', 'Fallback')).toBe('Fallback')
  })

  it('returns an empty server string as it is, as a nullish-coalescing chain does', () => {
    expect(apiErrorMessage(plain(400, { detail: '' }), 'detail', 'Fallback')).toBe('')
  })
})

describe('apiValidationMessage', () => {
  it('joins every message of every field into one line', () => {
    const error = axiosError(400, { errors: { Name: ['Name is required.', 'Name is too long.'], Level: ['Level must be 1 or more.'] } })

    expect(apiValidationMessage(error)).toBe('Name is required. Name is too long. Level must be 1 or more.')
  })

  it('is empty (not absent) for an `errors` object with nothing in it', () => {
    expect(apiValidationMessage(plain(400, { errors: {} }))).toBe('')
  })

  it.each([axiosError(400, { detail: 'No errors object' }), plain(400, { errors: 'x' }), plain(400, { errors: null }), new Error('x'), undefined])(
    'is undefined when the response has no validation errors (%j)',
    (error) => {
      expect(apiValidationMessage(error)).toBeUndefined()
    },
  )
})
