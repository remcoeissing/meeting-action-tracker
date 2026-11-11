import type { components } from './schema'

export type Committee = components['schemas']['Committee']
export type Member = components['schemas']['Member']
export type Meeting = components['schemas']['Meeting']
export type MeetingDetail = components['schemas']['MeetingDetail']
export type AgendaItem = components['schemas']['AgendaItem']
export type Action = components['schemas']['Action']
export type ActionInput = components['schemas']['ActionInput']
export type Status = components['schemas']['ActionStatus']

export const statusLabel = (status: Status) => status === 'InProgress' ? 'In progress' : status
export function localDate(date = new Date()): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}
export const overdue = (action: Action, today: string) => action.status !== 'Done' && action.dueDate < today

async function failure(response: Response): Promise<never> {
  const contentType = response.headers.get('content-type') ?? ''
  if (contentType.includes('json')) {
    const problem: components['schemas']['Problem'] = await response.json()
    const details = problem.errors ? Object.values(problem.errors).flat().join(' ') : problem.detail
    throw new Error(details || problem.title || `Request failed (${response.status}).`)
  }
  throw new Error(`Request failed (${response.status}). Please check that the API is running.`)
}

export async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const isForm = body instanceof FormData
  const response = await fetch(`/api/v1${path}`, {
    method,
    headers: body !== undefined && !isForm ? { 'Content-Type': 'application/json' } : undefined,
    body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
  })
  if (!response.ok) return failure(response)
  return response.status === 204 ? undefined as T : response.json()
}

export async function download(id: number, fileName: string): Promise<void> {
  const response = await fetch(`/api/v1/documents/${id}`)
  if (!response.ok) return failure(response)
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export const errorMessage = (error: unknown) =>
  error instanceof TypeError ? 'Cannot reach the API. Check the backend connection and try again.' :
  error instanceof Error ? error.message : 'An unexpected error occurred. Please try again.'
