# 🍪 fetch-cookie [![npm version](https://img.shields.io/npm/v/fetch-cookie?style=flat-square)](https://www.npmjs.org/package/fetch-cookie) [![Build status](https://img.shields.io/github/actions/workflow/status/valeriangalliat/fetch-cookie/test.yml?style=flat-square)](https://github.com/valeriangalliat/fetch-cookie/actions/workflows/test.yml)

> Decorator for a `fetch` function to support automatic cookie storage
> and population.

[Migrating from v1](#migrating-from-v1)

## Description

fetch-cookie wraps around a `fetch` function and **intercepts request
options and response objects to store received cookies and populate
request with the appropriate cookies**.

This library is developed with Node.js and `fetch` polyfill libraries such
as [node-fetch] in mind, since the browser version is supposed to let a
way [to include cookies in requests][include]. Compatibility may not be
guaranteed but as long as your library implements the [Fetch standard]
you should be fine. In case of incompatibilities, please create a new
issue.

[Fetch standard]: https://fetch.spec.whatwg.org/
[node-fetch]: https://www.npmjs.com/package/node-fetch
[include]: http://updates.html5rocks.com/2015/03/introduction-to-fetch#sending-credentials-with-a-fetch-request

Internally the plugin uses a cookie jar. You can insert your own
(details below) but [tough-cookie] is preferred.

[tough-cookie]: https://www.npmjs.com/package/tough-cookie

## Usage

With Node.js 18.3.0 and greater, using the native global `fetch`
function:

```js
import makeFetchCookie from 'fetch-cookie'

const fetchCookie = makeFetchCookie(fetch)
```

Or with node-fetch:

```js
import nodeFetch from 'node-fetch'
import fetchCookie from 'fetch-cookie'

const fetch = fetchCookie(nodeFetch)
```

### Custom cookie jar

If you want to customize the internal cookie jar instance (for example,
with a custom store), you can inject it as a second argument:

```js
import makeFetchCookie from 'fetch-cookie'

const fetchCookie = makeFetchCookie(fetch, new makeFetchCookie.toughCookie.CookieJar())
```

Here, we expose the tough-cookie version that we depend on internally so
you can just reuse it and don't end up accidentally bundling two
different versions. That being said you can use any version of
tough-cookie here, or even any compatible cookie jar.

This enables you to create multiple fetch-cookie instances that use
different cookie jars, essentially two different HTTP clients with
different login sessions on you backend (for example).

All calls to `fetch` will store and send back cookies according to the
URL.

If you use a cookie jar that is not tough-cookie, keep in mind that it
must implement this interface to be compatible:

```ts
interface CookieJar {
  getCookieString(currentUrl: string): Promise<string>
  setCookie(cookieString: string, currentUrl: string, opts: { ignoreError: boolean }): Promise
}
```

### Custom cookie jar with options

If you don't want a custom store and just want to pass [tough-cookie
options](https://github.com/salesforce/tough-cookie#cookiejarstore-options),
e.g. to allow cookies on `localhost`:

```js
import makeFetchCookie from 'fetch-cookie'

const fetchCookie = makeFetchCookie(fetch, new makeFetchCookie.toughCookie.CookieJar(undefined, {
  allowSpecialUseDomain: true
}))
```

Or with a custom store as well:

```js
import makeFetchCookie from 'fetch-cookie'
import FileCookieStore from 'file-cookie-store'

const store = new FileCookieStore('cookies.txt')

const fetchCookie = makeFetchCookie(fetch, new makeFetchCookie.toughCookie.CookieJar(store, {
  allowSpecialUseDomain: true
}))
```

### Ignoring errors

All errors when setting cookies are ignored by default. You can make it
throw errors in cookies by passing a third argument `ignoreError` (defaulting to `true`).

```js
import makeFetchCookie from 'fetch-cookie'

const fetchCookie = makeFetchCookie(fetch, new makeFetchCookie.toughCookie.CookieJar(), false)
```

When set to `false`, fetch-cookie will throw when an error occurs in
setting cookies and breaks the request and execution.

Otherwise, it silently ignores errors and continues to make
requests/redirections.

### Max redirects

Because we need to do our own [redirect implementation](#cookies-on-redirects),
the way to configure the max redirects is not going to be that of your
chosen `fetch` implementation, but custom to fetch-cookie.

We read a `maxRedirect` parameter for that. The default is 20.

```js
import makeFetchCookie from 'fetch-cookie'

const fetchCookie = makeFetchCookie(fetch)

await fetchCookie(url, { maxRedirect: 10 })
```

## Cookies on redirects

In order to handle cookies on redirects, we force the `redirect`
parameter to `manual`, and handle the redirections internally (according
to the original `redirect` value) instead of leaving it to the upstream
`fetch` implementation.

This allows us to hook into the redirect logic in order to store and
forward cookies accordingly.

This is useful for example when a login page sets a session cookie and
redirects to another page.

## Migrating from v1

The only breaking change with v2 is that the node-fetch wrapper (that
was handling redirects only with node-fetch nonstandard APIs) was
dropped and the redirects are now always handled by the main export.

```js
// If you were doing
const fetchCookie = require('fetch-cookie/node-fetch')

// Change it to
const fetchCookie = require('fetch-cookie')

// Or
import fetchCookie from 'fetch-cookie'
```

This also means that if you were *not* using the node-fetch wrapper and
were happy about cookies *not* being included in redirects, cookies are
now going to be stored and sent in redirects as well like they would in
the browser.

## Development

```sh
# Install dependencies
npm ci

# Allows to test node-fetch v2 as well as node-fetch v3
npm --prefix test/node_modules/node-fetch-2 ci

# Allows to test against Undici by removing the blacklisting of `Set-Cookie` headers
npm run patch-undici

npm run lint
npm run type-check

# Generates code in `esm` and `cjs` directories
npm run build

# Run tests (depends on the built code)
npm test

# Generate type declarations in the `types` directory
npm run type-declarations
```

## Credits

* 🥰 All the [contributors](https://github.com/valeriangalliat/fetch-cookie/graphs/contributors)
  to fetch-cookie!
* 😍 [node-fetch](https://github.com/node-fetch/node-fetch) for the
  redirect logic that we carefully mimic (sadly reimplementing this code
  was the only way to support cookies in redirects).
