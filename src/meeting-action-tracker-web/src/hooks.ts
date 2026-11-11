import { useState } from 'react'
import { errorMessage } from './api/client'

export function useOperation() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  async function run(operation: () => Promise<void>) {
    setBusy(true)
    setError('')
    try { await operation() }
    catch (error) { setError(errorMessage(error)) }
    finally { setBusy(false) }
  }
  return { busy, error, run }
}
