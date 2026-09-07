import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import { registerSyncOnReconnect } from './services/sync'

// The service worker used to cache every /api response for a day. That rule is gone, but a
// worker does not delete a runtime cache it has merely stopped declaring — so the stale copy
// would keep being served until it expired on its own. Swept once, here, where it is visible.
// Safe to delete along with this comment once no browser has been near the old build.
caches?.delete('api-cache')

const app = createApp(App)

app.use(createPinia())
app.use(router)
app.mount('#app')

registerSyncOnReconnect()
