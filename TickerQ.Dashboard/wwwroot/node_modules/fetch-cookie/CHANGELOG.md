# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.2.0] - 2024-02-01
* Export `CookieJar` interface. ([#81])

## [2.1.0] - 2022-07-12
* Explicitly export `package.json` to be compatible with bundlers. ([#72])

## [2.0.5] - 2022-05-26
* Change order of `exports` `default` in `package.json`. ([#72])

## [2.0.4] - 2022-05-25
* Support ESM and CJS hybrid types. ([#71])

## [2.0.3] - 2022-03-16
* Fix TypeScript types, and add a test to check that TypeScript is happy
  with fetch-cookie, node-fetch v2, v3 and undici. ([#70])

## [2.0.2] - 2022-03-15
* Make build script more portable.
* Backwards compatibility for environments not supporting the `exports`
  object in `package.json`, falling back on `main` and `module` fields. [#69]

## [2.0.1] - 2022-03-03
* Fix TypeScript types. ([#68])
* Because we can't specify different types for ESM and CJS, I chose to
  drop CJS support for TypeScript as I assume most TypeScript users will
  use this module with ESM. You can still `import fetchCookie = require('fetch-cookie')`
  but the type will be wrong (it's a function, not an object with a
  `default` property as TypeScript assumes).

## [2.0.0] - 2022-02-17
* Rewrite in TypeScript. ([#43])
* Hybrid support of ESM and CJS.
* Test against node-fetch v2, v3 and hypothetically WHATWG
  spec through [Undici](https://github.com/nodejs/undici).
* Reimplement the redirect logic based on latest most complete
  node-fetch implementation. ([#11], [#39], [#42], [#57])
* **Breaking:** the redirect logic is now included in the main
  export and the node-fetch wrapper (`require('fetch-cookie/node-fetch')`)
  was removed. Just `require('node-fetch')` and you're good to go with
  redirects!

## [1.0.1] - 2022-02-09
* Fix relative redirect URL with the fetch-cookie wrapper. ([#65])

## [1.0.0] - 2021-06-27
* Those changes are not breaking, but after 6 years of existence,
  it's probably a good time to release 1.0.0! 🎉
* Integrate with GitHub workflows. ([#63])
* Fix regression with empty cookie header. ([#63])
* Export `toughCookie` dependency on fetch-cookie module and instance. ([#44])

## [0.11.0] - 2020-11-02
* Fix types. ([#60])
* Improve types. ([#58])

## [0.10.1] - 2020-06-21
* Fix types. ([#56])

## [0.10.0] - 2020-06-16
* Allow ignoring `setCookie` errors. ([#53])
* Support 307 header when redirecting. ([#55])

## [0.9.1] - 2020-05-27
* Fix types. ([#51])

## [0.9.0] - 2020-05-25
* Add TypeScript types. ([#49])

## [0.8.0] - 2020-03-28
* Support more versions of tough-cookie, use `utils.promisify`. ([#46])

## [0.7.3] - 2019-07-07
* Update `tough-cookie` to 2.3.3.

## [0.7.2] - 2017-07-22
* Support headers object. ([#32], [#17])

## [0.7.1] - 2017-07-19
* Use async/await. ([#31])
* Improve tests and add CI. ([#30])
* Add cookie jar test. ([#20])
* Default for user options. ([#19])

## [0.7.0] - 2017-09-28
* Don't send empty cookies. ([#14])

## [0.6.0] - 2017-03-01
* Support node-fetch v2. ([#12])
* Add tests.

## [0.5.0] - 2017-02-27
* Fixes with redirect implementation. ([#13])

## [0.4.0] - 2017-02-05
* Handle cookies during redirections. ([#9])

## [0.3.0] - 2017-01-31
* Use final URL when storing cookies. ([#8])

## [0.2.0] - 2016-11-14
* Use `Object.assign` instead of the `sssign` module. ([#4])
* Update `tough-cookie` to 2.3.1. ([#3])
* ES5 compatibility. ([#2])
* Support multiple `Set-Cookie` headers. ([#1])

## [0.1.0] - 2015-04-04
* Initial release.

[Unreleased]: https://github.com/valeriangalliat/fetch-cookie/compare/v2.1.0...HEAD
[2.1.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v2.0.5...v2.1.0
[2.0.5]: https://github.com/valeriangalliat/fetch-cookie/compare/v2.0.4...v2.0.5
[2.0.4]: https://github.com/valeriangalliat/fetch-cookie/compare/v2.0.3...v2.0.4
[2.0.3]: https://github.com/valeriangalliat/fetch-cookie/compare/v2.0.2...v2.0.3
[2.0.2]: https://github.com/valeriangalliat/fetch-cookie/compare/v2.0.1...v2.0.2
[2.0.1]: https://github.com/valeriangalliat/fetch-cookie/compare/v2.0.0...v2.0.1
[2.0.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v1.0.1...v2.0.0
[1.0.1]: https://github.com/valeriangalliat/fetch-cookie/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.11.0...v1.0.0
[0.11.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.10.1...v0.11.0
[0.10.1]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.10.0...v0.10.1
[0.10.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.9.1...v0.10.0
[0.9.1]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.7.3...v0.8.0
[0.7.3]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.7.2...v0.7.3
[0.7.2]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.7.1...v0.7.2
[0.7.1]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/valeriangalliat/fetch-cookie/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/valeriangalliat/fetch-cookie/tree/v0.1.0

[#1]: https://github.com/valeriangalliat/fetch-cookie/issues/1
[#2]: https://github.com/valeriangalliat/fetch-cookie/pull/2
[#3]: https://github.com/valeriangalliat/fetch-cookie/issues/3
[#4]: https://github.com/valeriangalliat/fetch-cookie/pull/4
[#8]: https://github.com/valeriangalliat/fetch-cookie/pull/8
[#9]: https://github.com/valeriangalliat/fetch-cookie/pull/9
[#11]: https://github.com/valeriangalliat/fetch-cookie/issues/11
[#12]: https://github.com/valeriangalliat/fetch-cookie/issues/12
[#13]: https://github.com/valeriangalliat/fetch-cookie/issues/13
[#14]: https://github.com/valeriangalliat/fetch-cookie/pull/14
[#17]: https://github.com/valeriangalliat/fetch-cookie/issues/17
[#19]: https://github.com/valeriangalliat/fetch-cookie/pull/19
[#20]: https://github.com/valeriangalliat/fetch-cookie/pull/20
[#30]: https://github.com/valeriangalliat/fetch-cookie/pull/30
[#31]: https://github.com/valeriangalliat/fetch-cookie/pull/31
[#32]: https://github.com/valeriangalliat/fetch-cookie/pull/32
[#39]: https://github.com/valeriangalliat/fetch-cookie/issues/39
[#42]: https://github.com/valeriangalliat/fetch-cookie/issues/42
[#43]: https://github.com/valeriangalliat/fetch-cookie/issues/43
[#44]: https://github.com/valeriangalliat/fetch-cookie/pull/44
[#46]: https://github.com/valeriangalliat/fetch-cookie/pull/46
[#49]: https://github.com/valeriangalliat/fetch-cookie/pull/49
[#51]: https://github.com/valeriangalliat/fetch-cookie/pull/51
[#53]: https://github.com/valeriangalliat/fetch-cookie/pull/53
[#55]: https://github.com/valeriangalliat/fetch-cookie/pull/55
[#56]: https://github.com/valeriangalliat/fetch-cookie/pull/56
[#57]: https://github.com/valeriangalliat/fetch-cookie/issues/57
[#58]: https://github.com/valeriangalliat/fetch-cookie/pull/58
[#60]: https://github.com/valeriangalliat/fetch-cookie/pull/60
[#63]: https://github.com/valeriangalliat/fetch-cookie/pull/63
[#65]: https://github.com/valeriangalliat/fetch-cookie/issues/65
[#68]: https://github.com/valeriangalliat/fetch-cookie/issues/68
[#69]: https://github.com/valeriangalliat/fetch-cookie/issues/69
[#70]: https://github.com/valeriangalliat/fetch-cookie/issues/70
[#71]: https://github.com/valeriangalliat/fetch-cookie/issues/71
[#72]: https://github.com/valeriangalliat/fetch-cookie/issues/72
