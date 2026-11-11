export const dateLabel = (date: string) => new Date(`${date}T12:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
export const meetingTime = (date: string) => new Date(date).toLocaleString('en-GB', { dateStyle: 'medium', timeStyle: 'short' })
export function localDateTime(date: string) {
  const value = new Date(date)
  return new Date(value.getTime() - value.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}
