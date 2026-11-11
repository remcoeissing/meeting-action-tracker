import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import type { PageProps } from './App'
import { download, errorMessage, request, statusLabel } from './api/client'
import type { Action, ActionInput, AgendaItem, MeetingDetail, Status } from './api/client'
import { ActionTable } from './pages'
import { meetingTime } from './formatting'
import { meetingFields, text } from './forms'
import { useOperation } from './hooks'
import { Editor, Empty, ErrorAlert, Heading } from './ui'
import type { Field } from './ui'

type Editing = { kind: 'meeting' } | { kind: 'summary' } | { kind: 'agenda'; item?: AgendaItem } | { kind: 'action'; item?: Action }

export function MeetingPage({ data, today, refresh, id, version }: PageProps & { id: number; version: number }) {
  const [meeting, setMeeting] = useState<MeetingDetail | null>(null)
  const [loadError, setLoadError] = useState('')
  const [retry, setRetry] = useState(0)
  const [editing, setEditing] = useState<Editing | null>(null)
  const { busy, error, run } = useOperation()
  useEffect(() => {
    let active = true
    request<MeetingDetail>(`/meetings/${id}`)
      .then((value) => { if (active) { setMeeting(value); setLoadError('') } })
      .catch((error: unknown) => { if (active) setLoadError(errorMessage(error)) })
    return () => { active = false }
  }, [id, version, retry])
  if (!meeting) return <><ErrorAlert message={loadError} />{loadError ?
    <button onClick={() => setRetry((v) => v + 1)}>Retry meeting</button> : <div className="busy" role="status">Loading meeting...</div>}</>
  const committee = data.committees.find((item) => item.id === meeting.committeeId)
  const members = data.members.filter((member) => committee?.memberIds.includes(member.id))
  const actions = data.actions.filter((action) => action.meetingId === id)
  const move = (itemId: number, direction: number) => {
    const ids = meeting.agenda.map((item) => item.id)
    const index = ids.indexOf(itemId)
    const next = index + direction
    if (index < 0 || next < 0 || next >= ids.length) return
    ;[ids[index], ids[next]] = [ids[next], ids[index]]
    void run(async () => { await request(`/meetings/${id}/agenda/order`, 'PUT', { itemIds: ids }); refresh() })
  }
  let fields: Field[] = []
  if (editing?.kind === 'meeting') fields = meetingFields(meeting.committeeId, meeting)
  if (editing?.kind === 'summary') fields = [
    { name: 'summary', label: 'Meeting summary', type: 'textarea', value: meeting.summary, maxLength: 50000, wide: true },
  ]
  if (editing?.kind === 'agenda') fields = [
    { name: 'title', label: 'Agenda item title', required: true, value: editing.item?.title, wide: true },
    { name: 'description', label: 'Description / preparation notes', type: 'textarea', value: editing.item?.description, wide: true },
  ]
  if (editing?.kind === 'action') fields = [
    { name: 'title', label: 'Action title', required: true, value: editing.item?.title, wide: true },
    { name: 'description', label: 'Details', type: 'textarea', value: editing.item?.description, wide: true },
    { name: 'assigneeId', label: 'Assigned to', type: 'select', required: true, value: editing.item?.assigneeId,
      options: members.map((member) => ({ value: member.id, label: member.name })) },
    { name: 'dueDate', label: 'Due date', type: 'date', required: true, value: editing.item?.dueDate },
    { name: 'status', label: 'Status', type: 'select', required: true, value: editing.item?.status ?? 'Open',
      options: (['Open', 'InProgress', 'Done'] satisfies Status[]).map((value) => ({ value, label: statusLabel(value) })) },
  ]
  async function save(form: FormData) {
    if (!editing || !meeting) return
    if (editing.kind === 'meeting') await request(`/meetings/${id}`, 'PUT', {
      committeeId: meeting.committeeId, title: text(form, 'title'), scheduledAt: new Date(text(form, 'scheduledAt')).toISOString(),
      location: text(form, 'location'),
    })
    if (editing.kind === 'summary') await request(`/meetings/${id}/summary`, 'PUT', { summary: text(form, 'summary') })
    if (editing.kind === 'agenda') await request(editing.item ? `/agenda/${editing.item.id}` : `/meetings/${id}/agenda`,
      editing.item ? 'PUT' : 'POST', { title: text(form, 'title'), description: text(form, 'description') })
    if (editing.kind === 'action') {
      const status = text(form, 'status')
      if (status !== 'Open' && status !== 'InProgress' && status !== 'Done') throw new Error('Select a valid action status.')
      const body: ActionInput = { meetingId: id, title: text(form, 'title'), description: text(form, 'description'),
        assigneeId: Number(text(form, 'assigneeId')), dueDate: text(form, 'dueDate'), status }
      await request(editing.item ? `/actions/${editing.item.id}` : '/actions', editing.item ? 'PUT' : 'POST', body)
    }
    refresh()
  }
  return <>
    <div className="breadcrumbs"><a href="#/committees">Committees</a><span>/</span>
      <a href={`#/committees/${meeting.committeeId}`}>{meeting.committeeName}</a><span>/</span><span>Meeting</span></div>
    <Heading eyebrow={meeting.committeeName} title={meeting.title}
      description={`${meetingTime(meeting.scheduledAt)}${meeting.location ? ` / ${meeting.location}` : ''}`}>
      <button onClick={() => setEditing({ kind: 'meeting' })}>Edit meeting</button>
    </Heading>
    <ErrorAlert message={loadError || error} />
    {loadError && <button onClick={() => setRetry((v) => v + 1)}>Retry meeting refresh</button>}
    <div className="split"><section className="card">
      <div className="card-header"><h2>Meeting agenda</h2><button className="primary" onClick={() => setEditing({ kind: 'agenda' })}>+ Add item</button></div>
      {meeting.agenda.length ? meeting.agenda.map((item, index) => <article className="agenda" key={item.id}>
        <div className="agenda-top"><span className="agenda-number">{index + 1}</span><div className="agenda-copy">
          <h3>{item.title}</h3>{item.description && <p className="muted">{item.description}</p>}
          {item.documents.map((doc) => <div className="document" key={doc.id}>
            <span>{doc.fileName}<br /><small>{doc.size < 1024 ? `${doc.size} B` : `${(doc.size / 1024).toFixed(1)} KB`}</small></span>
            <div className="row"><button disabled={busy} onClick={() => void run(() => download(doc.id, doc.fileName))}>Download</button>
              <button className="danger" disabled={busy} aria-label={`Remove document ${doc.fileName}`} onClick={() => {
                if (window.confirm(`Permanently remove "${doc.fileName}"?`))
                  void run(async () => { await request(`/documents/${doc.id}`, 'DELETE'); refresh() })
              }}>Remove</button></div>
          </div>)}
          <Upload agendaId={item.id} refresh={refresh} />
        </div></div>
        <div className="toolbar">
          <button disabled={busy || index === 0} onClick={() => move(item.id, -1)} aria-label={`Move ${item.title} up`}>Move up</button>
          <button disabled={busy || index === meeting.agenda.length - 1} onClick={() => move(item.id, 1)} aria-label={`Move ${item.title} down`}>Move down</button>
          <button onClick={() => setEditing({ kind: 'agenda', item })}>Edit</button>
          <button className="danger" disabled={busy} onClick={() => {
            if (window.confirm(`Remove agenda item "${item.title}"? Remove its documents first.`))
              void run(async () => { await request(`/agenda/${item.id}`, 'DELETE'); refresh() })
          }}>Remove item</button>
        </div>
      </article>) : <Empty title="Set the agenda">Add discussion points and attach documents for preparation.</Empty>}
    </section><section className="card">
      <div className="card-header"><h2>Meeting summary</h2><button onClick={() => setEditing({ kind: 'summary' })}>Edit</button></div>
      {meeting.summary ? <p className="summary">{meeting.summary}</p> :
        <Empty title="Capture what matters">Write the key discussion points, decisions, and outcomes after the meeting.</Empty>}
      <div className="notice">Summaries are written manually. Follow-up work belongs in the action list below.</div>
    </section></div>
    <section className="card">
      <div className="card-header"><h2>Follow-up actions</h2>
        <button className="primary" disabled={!members.length} onClick={() => setEditing({ kind: 'action' })}>+ New action</button></div>
      {!members.length && <div className="notice">Add <a href={`#/committees/${meeting.committeeId}`}>committee members</a> before assigning an action.</div>}
      <ActionTable actions={actions} today={today} onEdit={(item) => setEditing({ kind: 'action', item })} />
    </section>
    {editing && <Editor title={editing.kind === 'meeting' ? 'Edit meeting' : editing.kind === 'summary' ? 'Edit summary' :
      editing.kind === 'agenda' ? editing.item ? 'Edit agenda item' : 'Add agenda item' : editing.item ? 'Edit action' : 'New action'}
      fields={fields} onSave={save} onClose={() => setEditing(null)} />}
  </>
}

function Upload({ agendaId, refresh }: { agendaId: number; refresh: () => void }) {
  const { busy, error, run } = useOperation()
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const body = new FormData(form)
    void run(async () => {
      const file = body.get('file')
      if (!(file instanceof File) || !file.size) throw new Error('Choose a non-empty document.')
      if (file.size > 20 * 1024 * 1024) throw new Error('Documents must be 20 MB or smaller.')
      await request(`/agenda/${agendaId}/documents`, 'POST', body)
      form.reset()
      refresh()
    })
  }
  return <form className="upload" onSubmit={submit}>
    <ErrorAlert message={error} />
    <label>Preparation document<input type="file" name="file" accept=".pdf,.docx,.xlsx,.pptx,.txt" required disabled={busy} /></label>
    <div className="row"><button disabled={busy}>{busy ? 'Uploading...' : 'Upload document'}</button>
      <small>PDF, DOCX, XLSX, PPTX, TXT / max. 20 MB</small></div>
  </form>
}
