var __defProp = Object.defineProperty;
var __defProps = Object.defineProperties;
var __getOwnPropDescs = Object.getOwnPropertyDescriptors;
var __getOwnPropSymbols = Object.getOwnPropertySymbols;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __propIsEnum = Object.prototype.propertyIsEnumerable;
var __defNormalProp = (obj, key, value) => key in obj ? __defProp(obj, key, { enumerable: true, configurable: true, writable: true, value }) : obj[key] = value;
var __spreadValues = (a, b) => {
  for (var prop in b || (b = {}))
    if (__hasOwnProp.call(b, prop))
      __defNormalProp(a, prop, b[prop]);
  if (__getOwnPropSymbols)
    for (var prop of __getOwnPropSymbols(b)) {
      if (__propIsEnum.call(b, prop))
        __defNormalProp(a, prop, b[prop]);
    }
  return a;
};
var __spreadProps = (a, b) => __defProps(a, __getOwnPropDescs(b));
var __async = (__this, __arguments, generator) => {
  return new Promise((resolve, reject) => {
    var fulfilled = (value) => {
      try {
        step(generator.next(value));
      } catch (e) {
        reject(e);
      }
    };
    var rejected = (value) => {
      try {
        step(generator.throw(value));
      } catch (e) {
        reject(e);
      }
    };
    var step = (x) => x.done ? resolve(x.value) : Promise.resolve(x.value).then(fulfilled, rejected);
    step((generator = generator.apply(__this, __arguments)).next());
  });
};
import * as tough from "tough-cookie";
import { splitCookiesString } from "set-cookie-parser";
function isDomainOrSubdomain(destination, original) {
  const orig = new URL(original).hostname;
  const dest = new URL(destination).hostname;
  return orig === dest || orig.endsWith(`.${dest}`);
}
const referrerPolicy = /* @__PURE__ */ new Set([
  "",
  "no-referrer",
  "no-referrer-when-downgrade",
  "same-origin",
  "origin",
  "strict-origin",
  "origin-when-cross-origin",
  "strict-origin-when-cross-origin",
  "unsafe-url"
]);
function parseReferrerPolicy(policyHeader) {
  const policyTokens = policyHeader.split(/[,\s]+/);
  let policy = "";
  for (const token of policyTokens) {
    if (token !== "" && referrerPolicy.has(token)) {
      policy = token;
    }
  }
  return policy;
}
function doNothing(init, name) {
}
function callDeleteMethod(init, name) {
  init.headers.delete(name);
}
function deleteFromObject(init, name) {
  const headers = init.headers;
  for (const key of Object.keys(headers)) {
    if (key.toLowerCase() === name) {
      delete headers[key];
    }
  }
}
function identifyDeleteHeader(init) {
  if (init.headers == null) {
    return doNothing;
  }
  if (typeof init.headers.delete === "function") {
    return callDeleteMethod;
  }
  return deleteFromObject;
}
const redirectStatus = /* @__PURE__ */ new Set([301, 302, 303, 307, 308]);
function isRedirect(status) {
  return redirectStatus.has(status);
}
function handleRedirect(fetchImpl, init, response) {
  return __async(this, null, function* () {
    var _a, _b, _c;
    switch ((_a = init.redirect) != null ? _a : "follow") {
      case "error":
        throw new TypeError(`URI requested responded with a redirect and redirect mode is set to error: ${response.url}`);
      case "manual":
        return response;
      case "follow":
        break;
      default:
        throw new TypeError(`Invalid redirect option: ${init.redirect}`);
    }
    const locationUrl = response.headers.get("location");
    if (locationUrl === null) {
      return response;
    }
    const requestUrl = response.url;
    const redirectUrl = new URL(locationUrl, requestUrl).toString();
    const redirectCount = (_b = init.redirectCount) != null ? _b : 0;
    const maxRedirect = (_c = init.maxRedirect) != null ? _c : 20;
    if (redirectCount >= maxRedirect) {
      throw new TypeError(`Reached maximum redirect of ${maxRedirect} for URL: ${requestUrl}`);
    }
    init = __spreadProps(__spreadValues({}, init), {
      redirectCount: redirectCount + 1
    });
    const deleteHeader = identifyDeleteHeader(init);
    if (!isDomainOrSubdomain(requestUrl, redirectUrl)) {
      for (const name of ["authorization", "www-authenticate", "cookie", "cookie2"]) {
        deleteHeader(init, name);
      }
    }
    const maybeNodeStreamBody = init.body;
    const maybeStreamBody = init.body;
    if (response.status !== 303 && init.body != null && (typeof maybeNodeStreamBody.pipe === "function" || typeof maybeStreamBody.pipeTo === "function")) {
      throw new TypeError("Cannot follow redirect with body being a readable stream");
    }
    if (response.status === 303 || (response.status === 301 || response.status === 302) && init.method === "POST") {
      init.method = "GET";
      init.body = void 0;
      deleteHeader(init, "content-length");
    }
    if (response.headers.has("referrer-policy")) {
      init.referrerPolicy = parseReferrerPolicy(response.headers.get("referrer-policy"));
    }
    return yield fetchImpl(redirectUrl, init);
  });
}
function addCookiesToRequest(input, init, cookie) {
  if (cookie === "") {
    return init;
  }
  const maybeRequest = input;
  const maybeHeaders = init.headers;
  if (maybeRequest.headers && typeof maybeRequest.headers.append === "function") {
    maybeRequest.headers.append("cookie", cookie);
  } else if (maybeHeaders && typeof maybeHeaders.append === "function") {
    maybeHeaders.append("cookie", cookie);
  } else {
    init = __spreadProps(__spreadValues({}, init), { headers: __spreadProps(__spreadValues({}, init.headers), { cookie }) });
  }
  return init;
}
function getCookiesFromResponse(response) {
  const maybeNodeFetchHeaders = response.headers;
  if (typeof maybeNodeFetchHeaders.getAll === "function") {
    return maybeNodeFetchHeaders.getAll("set-cookie");
  }
  if (typeof maybeNodeFetchHeaders.raw === "function") {
    const headers = maybeNodeFetchHeaders.raw();
    if (Array.isArray(headers["set-cookie"])) {
      return headers["set-cookie"];
    }
    return [];
  }
  const cookieString = response.headers.get("set-cookie");
  if (cookieString !== null) {
    return splitCookiesString(cookieString);
  }
  return [];
}
function fetchCookie(fetch, jar, ignoreError = true) {
  const actualFetch = fetch;
  const actualJar = jar != null ? jar : new tough.CookieJar();
  function fetchCookieWrapper(input, init) {
    return __async(this, null, function* () {
      var _a, _b;
      const originalInit = init != null ? init : {};
      init = __spreadProps(__spreadValues({}, init), { redirect: "manual" });
      const requestUrl = typeof input === "string" ? input : (_a = input.url) != null ? _a : input.href;
      const cookie = yield actualJar.getCookieString(requestUrl);
      init = addCookiesToRequest(input, init, cookie);
      const response = yield actualFetch(input, init);
      const cookies = getCookiesFromResponse(response);
      yield Promise.all(cookies.map((cookie2) => __async(this, null, function* () {
        return yield actualJar.setCookie(cookie2, response.url, { ignoreError });
      })));
      if (((_b = init.redirectCount) != null ? _b : 0) > 0) {
        Object.defineProperty(response, "redirected", { value: true });
      }
      if (!isRedirect(response.status)) {
        return response;
      }
      return yield handleRedirect(fetchCookieWrapper, originalInit, response);
    });
  }
  fetchCookieWrapper.toughCookie = tough;
  return fetchCookieWrapper;
}
fetchCookie.toughCookie = tough;
export {
  fetchCookie as default
};
