import { useEffect, useState } from 'react'
import { request, errorMessage, localDate } from './api/client'
import type { Committee, Member, Meeting, Action } from './api/client'
import { ErrorAlert } from './ui'
import { Dashboard, Committees, CommitteePage, Members } from './pages'
import { MeetingPage } from './meeting'

export type Workspace = { committees: Committee[]; members: Member[]; meetings: Meeting[]; actions: Action[] }
export type PageProps = { data: Workspace; refresh: () => void; today: string }

function App() {
  const [route, setRoute] = useState(window.location.hash || '#/')
  const [version, setVersion] = useState(0)
  const [today, setToday] = useState(localDate)
  const [result, setResult] = useState<{ key: string; data: Workspace | null; error: string }>({ key: '', data: null, error: '' })
  const loadKey = `${version}:${today}`
  const data = result.data
  const error = result.key === loadKey ? result.error : ''
  const loading = result.key !== loadKey
  const [theme, setTheme] = useState(document.documentElement.dataset.theme ?? 'light')
  useEffect(() => {
    const navigate = () => { setRoute(window.location.hash || '#/'); window.scrollTo(0, 0) }
    window.addEventListener('hashchange', navigate)
    const timer = window.setInterval(() => setToday(localDate()), 30000)
    return () => { window.removeEventListener('hashchange', navigate); window.clearInterval(timer) }
  }, [])
  useEffect(() => {
    let active = true
    Promise.all([
      request<Committee[]>('/committees'), request<Member[]>('/members'),
      request<Meeting[]>('/meetings'), request<Action[]>(`/actions?today=${today}`),
    ]).then(([committees, members, meetings, actions]) => {
      if (active) setResult({ key: loadKey, data: { committees, members, meetings, actions }, error: '' })
    }).catch((error: unknown) => {
      if (active) setResult((previous) => ({ key: loadKey, data: previous.data, error: errorMessage(error) }))
    })
    return () => { active = false }
  }, [loadKey, today])
  const props: PageProps | null = data ? { data, today, refresh: () => setVersion((v) => v + 1) } : null
  const parts = route.slice(2).split('/')
  const section = parts[0]
  function changeTheme() {
    const next = theme === 'dark' ? 'light' : 'dark'
    document.documentElement.dataset.theme = next
    const url = new URL(window.location.href)
    url.searchParams.set('scoutTheme', next)
    window.history.replaceState(null, '', url)
    setTheme(next)
  }
  const nav = [
    { href: '#/', label: 'Action dashboard', icon: '=', active: !section },
    { href: '#/committees', label: 'Committees', icon: '#', active: section === 'committees' || section === 'meetings' },
    { href: '#/members', label: 'Members', icon: '@', active: section === 'members' },
  ]
  return <div className="shell">
    <a className="skip" href="#main" onClick={(event) => {
      event.preventDefault()
      document.getElementById('main')?.focus()
    }}>Skip to content</a>
    <aside className="sidebar">
      <a className="brand" href="#/"><span className="brand-icon" aria-hidden="true">M</span><span>Meeting<br />Action Tracker</span></a>
      <nav aria-label="Main navigation">{nav.map((item) =>
        <a key={item.href} href={item.href} className={`nav-link ${item.active ? 'active' : ''}`} aria-current={item.active ? 'page' : undefined}>
          <span className="nav-icon" aria-hidden="true">{item.icon}</span>{item.label}</a>)}</nav>
      <div className="sidebar-bottom">
        <div className="local-label"><strong>Local workspace</strong>No sign-in. For local prototype use only.</div>
        <button className="theme-button quiet" onClick={changeTheme}>{theme === 'dark' ? 'Light' : 'Dark'} appearance</button>
      </div>
    </aside>
    <div className="workspace">
      <div className="topbar"><span>Committee workspace</span><small>Prepare. Discuss. Follow through.</small></div>
      <main className="content" id="main" tabIndex={-1}>
        <ErrorAlert message={error} />
        {error && <button onClick={() => setVersion((v) => v + 1)}>Retry connection</button>}
        {loading && <div role="status" className={data ? 'notice' : 'busy'}>{data ? 'Refreshing workspace...' : 'Loading workspace...'}</div>}
        {props && (!section ? <Dashboard {...props} /> :
          section === 'committees' && !parts[1] ? <Committees {...props} /> :
          section === 'committees' && /^\d+$/.test(parts[1] ?? '') ? <CommitteePage key={route} {...props} id={Number(parts[1])} /> :
          section === 'members' ? <Members {...props} /> :
          section === 'meetings' && /^\d+$/.test(parts[1] ?? '') ? <MeetingPage key={route} {...props} id={Number(parts[1])} version={version} /> :
          <div className="card"><h1>Page not found</h1><a href="#/">Return to dashboard</a></div>)}
      </main>
    </div>
  </div>
}

export default App
