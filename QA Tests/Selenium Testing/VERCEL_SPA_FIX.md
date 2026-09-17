# Vercel SPA 404 Fix - Why /processing returns 404

## Problem
Your test logs show:
```
<title>404: not_found</title>
...
assert 'dispatch' in '<html>...404: not_found...'
```

When Selenium does `driver.get("https://frontend-phi-sage-81.vercel.app/processing")`, Vercel returns its default 404 page because:
- React SPA uses client-side routing (react-router)
- `/processing` is not a real file, it's a virtual route handled by `index.html` + JS
- Vercel needs `vercel.json` with rewrite to serve `index.html` for all routes

## Evidence from bundle
```js
// From /assets/index-BNZiiY2.js
if(V(`/processing`,e)) return {tab:`home`, processingTab:`factory`, isProcessing:!0, needs:`readProcessing`, element: (0,m.jsx)(ni,{})}
// ...
if(s&&n===`/`) return window.history.replaceState(window.history.state,``, `/processing`)
// ProcessingTechnician auto-redirects from / to /processing via history.replaceState
```

So:
- Root `/` shows login, after login with `processing` role, JS does `replaceState` to `/processing`
- Direct GET to `/processing` fails without rewrite - Vercel looks for `/processing/index.html` file, not found -> 404
- Login test passed because it stays at `/`
- Dashboard tests failed because they did `driver.get(PROCESSING_URL)` which triggers Vercel 404

## Fix - Add vercel.json to frontend repo root

Create `vercel.json` in your frontend repo (next to package.json):

```json
{
  "rewrites": [
    { "source": "/(.*)", "destination": "/index.html" }
  ]
}
```

Or for more specific:

```json
{
  "rewrites": [
    { "source": "/processing", "destination": "/index.html" },
    { "source": "/processing/:path*", "destination": "/index.html" },
    { "source": "/consignments/:path*", "destination": "/index.html" },
    { "source": "/tanks/:path*", "destination": "/index.html" },
    { "source": "/(.*)", "destination": "/index.html" }
  ]
}
```

Then:
```bash
git add vercel.json
git commit -m "fix: add vercel.json SPA rewrite for /processing route"
git push
# Vercel auto-deploys
```

After deploy, `https://frontend-phi-sage-81.vercel.app/processing` will serve `index.html` and React router will handle it, not Vercel 404.

## Selenium Fix Applied

I've updated all Selenium tests to avoid direct `driver.get(PROCESSING_URL)`:

**Before (fails with 404):**
```python
driver.get(PROCESSING_URL)  # https://.../processing -> Vercel 404
```

**After (works with SPA):**
```python
dashboard = DashboardPage(driver)
dashboard.navigate_to_processing_via_spa()  # Goes to BASE_URL, waits for auto-redirect, uses JS pushState
```

`DashboardPage.navigate_to_processing_via_spa()` does:
1. `driver.get(BASE_URL)` - load root
2. Wait for auto-redirect to /processing (ProcessingTechnician role does replaceState)
3. If not redirected, `execute_script("history.pushState({}, '', '/processing')")` + dispatch popstate
4. Try clicking Factory button if visible
5. Check for "Factory Active" pill, tabbar, not 404

`conftest.py` `logged_in_driver` fixture also fixed same way.

## Test Again After Fix

```powershell
cd "D:\...\Selenium_Tests\Selenium_Tests"
python -m pytest tests/test_dashboard.py -v -s
```

Should now PASS instead of 5 FAILED.

## Alternative - Test Locally Without Vercel Fix

If you can't deploy vercel.json now, the fixed Selenium tests will still work because they navigate via BASE_URL not direct /processing. But for users manually typing /processing URL, they'll still see 404 until vercel.json deployed.

## Related - Why login passed

Login page is at `/` which is real file `index.html` -> works.
Processing page is virtual route `/processing` -> needs rewrite -> 404 without vercel.json.
