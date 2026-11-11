import { useState } from 'react'
import type { PageProps } from './App'
import { request, overdue, statusLabel } from './api/client'
import type { Action, Committee, Member, Meeting } from './api/client'
import { Editor, Empty, ErrorAlert, Heading } from './ui'
import type { Field } from './ui'
import { dateLabel, meetingTime } from './formatting'
import { meetingFields, text } from './forms'
import { useOperation } from './hooks'

export function ActionTable({ actions, today, onEdit }: { actions: Action[]; today: string; onEdit?: (action: Action) => void }) {
  return actions.length ? <div className="table-wrap"><table>
    <thead><tr><th>Action / meeting</th><th>Assigned to</th><th>Due date</th><th>Status</th>{onEdit && <th><span className="sr-only">Edit</span></th>}</tr></thead>
    <tbody>{actions.map((action) => <tr key={action.id}>
      <td><div className="action-title">{action.title}</div><a className="muted" href={`#/meetings/${action.meetingId}`}>{action.committeeName} / {action.meetingTitle}</a>
        {action.description && <div className="muted">{action.description}</div>}</td>
      <td>{action.assigneeName}</td>
      <td>{dateLabel(action.dueDate)}{overdue(action, today) && <div><span className="badge overdue">Overdue</span></div>}</td>
      <td><span className={`badge ${action.status === 'Done' ? 'done' : ''}`}>{statusLabel(action.status)}</span></td>
      {onEdit && <td><button onClick={() => onEdit(action)} aria-label={`Edit action: ${action.title}`}>Edit</button></td>}
    </tr>)}</tbody>
  </table></div> : <Empty title="Nothing here yet">Actions matching this view will appear here.</Empty>
}

export function Dashboard({ data, today }: PageProps) {
  const [committee, setCommittee] = useState('')
  const [assignee, setAssignee] = useState('')
  const [status, setStatus] = useState('outstanding')
  const outstanding = data.actions.filter((action) => action.status !== 'Done')
  const late = outstanding.filter((action) => overdue(action, today))
  const dueToday = outstanding.filter((action) => action.dueDate === today)
  const actions = data.actions.filter((action) =>
    (!committee || action.committeeId === Number(committee)) &&
    (!assignee || action.assigneeId === Number(assignee)) &&
    (status === 'all' || (status === 'outstanding' ? action.status !== 'Done' :
      status === 'overdue' ? overdue(action, today) : action.status === status)))
  return <>
    <Heading eyebrow="Follow through" title="Action dashboard" description="A clear view of what comes next, across every committee.">
      <a href="#/committees" className="button-link">Browse committees</a>
    </Heading>
    <div className="stats">
      <div className="stat"><small>OUTSTANDING ACTIONS</small><strong>{outstanding.length}</strong><small>Open or in progress</small></div>
      <div className="stat attention"><small>OVERDUE</small><strong>{late.length}</strong><small>Ready for a follow-up</small></div>
      <div className="stat"><small>DUE TODAY</small><strong>{dueToday.length}</strong><small>{dateLabel(today)}</small></div>
    </div>
    <section className="card">
      <div className="card-header"><h2>Action overview</h2><span className="badge">{actions.length} actions</span></div>
      <div className="filters">
        <label>Committee<select value={committee} onChange={(e) => setCommittee(e.target.value)}><option value="">All committees</option>
          {data.committees.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Assignee<select value={assignee} onChange={(e) => setAssignee(e.target.value)}><option value="">All members</option>
          {data.members.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Status<select value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="outstanding">Outstanding</option><option value="overdue">Overdue</option>
          <option value="Open">Open</option><option value="InProgress">In progress</option><option value="Done">Done</option><option value="all">All statuses</option>
        </select></label>
        <button onClick={() => { setCommittee(''); setAssignee(''); setStatus('outstanding') }}>Reset</button>
      </div>
      <ActionTable actions={actions} today={today} />
    </section>
    <div className="notice">Actions are created and updated in their meeting. Open a meeting from an action above, or browse your committees to get started.</div>
  </>
}

const committeeFields = (committee?: Committee): Field[] => [
  { name: 'name', label: 'Committee name', required: true, value: committee?.name, wide: true },
  { name: 'description', label: 'Subject area / description', type: 'textarea', value: committee?.description, wide: true },
]

export function Committees({ data, refresh }: PageProps) {
  const [editing, setEditing] = useState<Committee | 'new' | null>(null)
  return <>
    <Heading eyebrow="Work together" title="Committees" description="Give every subject a place, and every meeting a purpose.">
      <button className="primary" onClick={() => setEditing('new')}>+ New committee</button>
    </Heading>
    {data.committees.length ? <div className="cards-grid">{data.committees.map((committee) =>
      <article className="card" key={committee.id}>
        <div className="eyebrow">Committee</div><h2>{committee.name}</h2>
        <p className="muted">{committee.description || 'No description yet.'}</p>
        <small>{committee.memberIds.length} members / {data.meetings.filter((meeting) => meeting.committeeId === committee.id).length} meetings</small>
        <div className="row"><a href={`#/committees/${committee.id}`}>Open committee &rarr;</a>
          <button onClick={() => setEditing(committee)} aria-label={`Edit committee: ${committee.name}`}>Edit</button></div>
      </article>)}</div> :
      <section className="card"><Empty title="Start with a committee">Create your first committee, then add members and schedule a meeting.</Empty></section>}
    {editing && <Editor title={editing === 'new' ? 'New committee' : 'Edit committee'}
      fields={committeeFields(editing === 'new' ? undefined : editing)} onClose={() => setEditing(null)}
      onSave={async (form) => {
        await request(editing === 'new' ? '/committees' : `/committees/${editing.id}`, editing === 'new' ? 'POST' : 'PUT',
          { name: text(form, 'name'), description: text(form, 'description') })
        refresh()
      }} />}
  </>
}

export function Members({ data, refresh }: PageProps) {
  const [editing, setEditing] = useState<Member | 'new' | null>(null)
  const { error, busy, run } = useOperation()
  return <>
    <Heading eyebrow="People" title="Members" description="Manage the people who participate and take ownership of actions.">
      <button className="primary" onClick={() => setEditing('new')}>+ New member</button>
    </Heading>
    <ErrorAlert message={error} />
    <section className="card">{data.members.length ? data.members.map((member) => <div className="list-row" key={member.id}>
      <div><h3>{member.name}</h3><small>{data.committees.filter((item) => item.memberIds.includes(member.id)).map((item) => item.name).join(', ') || 'Not yet assigned to a committee'}</small></div>
      <div className="row"><button onClick={() => setEditing(member)}>Edit</button>
        <button className="danger" disabled={busy} onClick={() => {
          if (window.confirm(`Delete ${member.name}? Only members with no committee memberships or assigned actions can be deleted.`))
            void run(async () => { await request(`/members/${member.id}`, 'DELETE'); refresh() })
        }}>Delete</button></div>
    </div>) : <Empty title="Meet your members">Add people here, then select them from a committee's membership list.</Empty>}</section>
    {editing && <Editor title={editing === 'new' ? 'New member' : 'Edit member'}
      fields={[{ name: 'name', label: 'Display name', required: true, value: editing === 'new' ? '' : editing.name, wide: true }]}
      onClose={() => setEditing(null)} onSave={async (form) => {
        await request(editing === 'new' ? '/members' : `/members/${editing.id}`, editing === 'new' ? 'POST' : 'PUT', { name: text(form, 'name') })
        refresh()
      }} />}
  </>
}

export function CommitteePage({ data, refresh, id }: PageProps & { id: number }) {
  const committee = data.committees.find((item) => item.id === id)
  const [editor, setEditor] = useState<'meeting' | 'member' | null>(null)
  const [selectedMember, setSelectedMember] = useState('')
  const { busy, error, run } = useOperation()
  if (!committee) return <Empty title="Committee not found"><a href="#/committees">Back to committees</a></Empty>
  const meetings = data.meetings.filter((item) => item.committeeId === id)
  const members = data.members.filter((member) => committee.memberIds.includes(member.id))
  const available = data.members.filter((member) => !committee.memberIds.includes(member.id))
  return <>
    <div className="breadcrumbs"><a href="#/committees">Committees</a><span>/</span><span>{committee.name}</span></div>
    <Heading eyebrow="Committee workspace" title={committee.name} description={committee.description}>
      <button className="primary" onClick={() => setEditor('meeting')}>+ Schedule meeting</button>
    </Heading>
    <ErrorAlert message={error} />
    <div className="split"><section className="card">
      <div className="card-header"><h2>Meetings</h2><span className="badge">{meetings.length}</span></div>
      {meetings.length ? meetings.map((meeting) => <div className="list-row" key={meeting.id}>
        <div><h3><a href={`#/meetings/${meeting.id}`}>{meeting.title}</a></h3>
          <small>{meetingTime(meeting.scheduledAt)}{meeting.location && ` / ${meeting.location}`}</small></div>
        <a href={`#/meetings/${meeting.id}`}>Open &rarr;</a>
      </div>) : <Empty title="Make room for a conversation">Schedule the first meeting to start preparing an agenda.</Empty>}
    </section><section className="card">
      <h2>Committee members</h2>
      {members.length ? members.map((member) => <div className="list-row" key={member.id}><span>{member.name}</span>
        <button className="quiet" disabled={busy} aria-label={`Remove ${member.name} from committee`} onClick={() => {
          if (window.confirm(`Remove ${member.name} from this committee? Existing action assignments prevent removal.`))
            void run(async () => { await request(`/committees/${id}/members/${member.id}`, 'DELETE'); refresh() })
        }}>Remove</button></div>) : <p className="muted">No members yet. Add members before assigning actions.</p>}
      <form className="fields" onSubmit={(event) => {
        event.preventDefault()
        void run(async () => { await request(`/committees/${id}/members/${Number(selectedMember)}`, 'PUT'); setSelectedMember(''); refresh() })
      }}>
        <label>Add existing member<select required value={selectedMember} onChange={(e) => setSelectedMember(e.target.value)}>
          <option value="">Select a member</option>{available.map((member) => <option key={member.id} value={member.id}>{member.name}</option>)}
        </select></label><button disabled={busy || !selectedMember}>Add to committee</button>
        <button type="button" className="quiet" onClick={() => setEditor('member')}>+ Create a new member</button>
      </form>
    </section></div>
    {editor === 'meeting' && <Editor title="Schedule meeting" fields={meetingFields(id)} onClose={() => setEditor(null)}
      onSave={async (form) => {
        const meeting = await request<Meeting>('/meetings', 'POST', { committeeId: id, title: text(form, 'title'),
          scheduledAt: new Date(text(form, 'scheduledAt')).toISOString(), location: text(form, 'location') })
        refresh()
        window.location.hash = `/meetings/${meeting.id}`
      }} />}
    {editor === 'member' && <Editor title="Create member"
      fields={[{ name: 'name', label: 'Display name', required: true, wide: true }]}
      note="After creating the member, select them from the existing-member list to add them to this committee."
      onClose={() => setEditor(null)} onSave={async (form) => {
        await request<Member>('/members', 'POST', { name: text(form, 'name') })
        refresh()
      }} />}
  </>
}
