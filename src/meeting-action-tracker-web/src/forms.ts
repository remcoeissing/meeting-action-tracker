import type { Meeting } from './api/client'
import { localDateTime } from './formatting'
import type { Field } from './ui'

export const text = (data: FormData, name: string) => {
  const value = data.get(name)
  if (typeof value !== 'string') throw new Error(`Missing ${name}.`)
  return value
}

export function meetingFields(committeeId: number, meeting?: Meeting): Field[] {
  return [
    { name: 'title', label: 'Meeting title', required: true, value: meeting?.title, wide: true },
    { name: 'scheduledAt', label: 'Date and time (your timezone)', type: 'datetime-local', required: true, value: meeting ? localDateTime(meeting.scheduledAt) : '' },
    { name: 'location', label: 'Location', maxLength: 500, value: meeting?.location },
    { name: 'committeeId', label: 'Committee', type: 'select', required: true, value: committeeId, options: [{ value: committeeId, label: meeting?.committeeName ?? 'Current committee' }], wide: true },
  ]
}
