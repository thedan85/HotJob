// Browser-side auth helper: fetch calls originate from the browser so the
// HttpOnly refresh cookie (Set-Cookie on login) is stored and sent back.
// Server-side HttpClient can never deliver Set-Cookie to the browser.
window.hotjobAuth = {
  async request(path, options) {
    const response = await fetch(path, {
      credentials: "same-origin",
      ...options,
      headers: { "Content-Type": "application/json", ...(options?.headers ?? {}) },
    });
    let errors = [];
    let data = null;
    const text = await response.text();
    if (text) {
      try {
        const parsed = JSON.parse(text);
        if (Array.isArray(parsed)) errors = parsed;
        else data = parsed;
      } catch {
        errors = [text];
      }
    }
    return { ok: response.ok, status: response.status, data, errors };
  },
  login(email, password) {
    return this.request("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    });
  },
  me() {
    return this.request("/api/auth/me", { method: "GET" });
  },
  logout() {
    return this.request("/api/auth/logout", { method: "POST" });
  },
};
