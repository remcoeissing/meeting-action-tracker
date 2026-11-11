import { useEffect, useRef } from 'react'
import type { ReactNode, FormEvent } from 'react'
import { useOperation } from './hooks'

export function Heading({ eyebrow, title, description, children }: {
  eyebrow: string; title: string; description?: string; children?: ReactNode
}) {
  return <header className="page-heading"><div>
    <div className="eyebrow">{eyebrow}</div><h1>{title}</h1>
    {description && <p>{description}</p>}
  </div>{children}</header>
}

export function Empty({ title, children }: { title: string; children: ReactNode }) {
  return <div className="empty"><span className="empty-mark" aria-hidden="true">+</span>
    <h3>{title}</h3><div>{children}</div></div>
}

export function ErrorAlert({ message }: { message: string }) {
  return message ? <div className="alert" role="alert">{message}</div> : null
}

export type Field = {
  name: string
  label: string
  type?: 'text' | 'textarea' | 'select' | 'date' | 'datetime-local'
  value?: string | number
  required?: boolean
  maxLength?: number
  options?: { value: string | number; label: string }[]
  wide?: boolean
}

export function Editor({ title, fields, onSave, onClose, note }: {
  title: string; fields: Field[]; onSave: (data: FormData) => Promise<void>
  onClose: () => void; note?: string
}) {
  const ref = useRef<HTMLDialogElement>(null)
  const { busy, error, run } = useOperation()
  useEffect(() => {
    const dialog = ref.current
    dialog?.showModal()
    return () => dialog?.close()
  }, [])
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    void run(async () => { await onSave(data); onClose() })
  }
  return <dialog ref={ref} className="modal" aria-labelledby="editor-title"
    onCancel={(event) => { if (busy) event.preventDefault(); else onClose() }}>
    <div className="modal-head"><h2 id="editor-title">{title}</h2>
      <button type="button" disabled={busy} onClick={onClose} aria-label="Close dialog">X</button></div>
    {note && <p className="muted">{note}</p>}
    <ErrorAlert message={error} />
    <form onSubmit={submit}>
      <fieldset disabled={busy} style={{ border: 0, padding: 0, margin: 0 }}>
        <div className="fields two">{fields.map((field) => <label key={field.name} className={field.wide ? 'wide' : ''}>
          {field.label}{field.required && ' *'}
          {field.type === 'select' ? <select name={field.name} required={field.required} defaultValue={field.value ?? ''}>
            <option value="" disabled>Select...</option>
            {field.options?.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select> : field.type === 'textarea' ?
            <textarea name={field.name} defaultValue={field.value ?? ''} required={field.required} maxLength={field.maxLength ?? 4000} rows={5} /> :
            <input name={field.name} type={field.type ?? 'text'} defaultValue={field.value ?? ''} required={field.required}
              maxLength={field.maxLength ?? 200} />}
        </label>)}</div>
      </fieldset>
      <div className="form-actions"><button type="button" disabled={busy} onClick={onClose}>Cancel</button>
        <button className="primary" disabled={busy}>{busy ? 'Saving...' : 'Save changes'}</button></div>
    </form>
  </dialog>
}
