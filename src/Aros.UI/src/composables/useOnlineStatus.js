import { ref, onMounted, onUnmounted } from 'vue'

const PING_URL = '/api/system/ping'
const PING_INTERVAL_MS = 15_000

export function useOnlineStatus() {
  const isOnline = ref(false)
  let intervalId = null

  async function check() {
    try {
      const res = await fetch(PING_URL, { method: 'HEAD', cache: 'no-store' })
      isOnline.value = res.ok
    } catch {
      isOnline.value = false
    }
  }

  onMounted(() => {
    check()
    intervalId = setInterval(check, PING_INTERVAL_MS)
    window.addEventListener('online', check)
    window.addEventListener('offline', check)
  })

  onUnmounted(() => {
    clearInterval(intervalId)
    window.removeEventListener('online', check)
    window.removeEventListener('offline', check)
  })

  return { isOnline }
}
