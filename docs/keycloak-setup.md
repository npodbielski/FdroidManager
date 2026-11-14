# Keycloak Setup Guide for F-Droid Manager

## 1. Create Realm
1. Go to Keycloak admin console: `https://keycloak.example.com`
2. Click **Administration Console**
3. Click **Create realm**
4. Enter realm name: `apps`
5. Click **Create**

## 2. Create Client
1. Go to **Clients** → **Create client**
2. Enter **Client ID**: `fdroid-mgr`
3. Click **Next**
4. Set **Client authentication**: OFF (the manager is a public PKCE client —
   it never sends a client secret)
5. Enable **Standard flow**: ON
6. Enable **Direct access grants**: OFF (unless needed)
7. Click **Save**

## 3. Configure Client Settings
1. Go to **Settings** tab
2. Set **Valid redirect URIs**: `https://fdroid-manager.example.com/*`
3. Set **Valid post logout redirect URIs**: `https://fdroid-manager.example.com/*`
4. Set **Web origins**: `https://fdroid-manager.example.com`
5. Click **Save**

## 4. Configure F-Droid Manager
1. Open `https://fdroid-manager.example.com/ui`
2. Click "Sign in" to start the first-run wizard
3. Enter your Keycloak settings:
   - **Issuer**: `https://keycloak.example.com/realms/apps`
   - **Client ID**: `fdroid-mgr`
   - **Scopes**: `openid profile email`
4. Save settings — **no container restart required**

## 5. Verify Login
1. Go to `https://fdroid-manager.example.com/ui`
2. Click "Sign in"
3. Should redirect to Keycloak login page
4. After successful login, should redirect back to the app

## Troubleshooting
### Login Fails
- Read the reason under **Sign in with SSO** — the manager keeps the last
  sign-in failure there (in memory, cleared by the next successful sign-in)
- Check that `FDROID_PUBLIC_URL` matches the domain you're accessing
- Verify Keycloak **Valid redirect URIs** include `https://fdroid-manager.example.com/*`
- Check container logs: `podman logs fdroidmgr`

### Keycloak Returns Error
- Check Keycloak server logs for detailed error messages
- Ensure the realm name matches (`apps`)
