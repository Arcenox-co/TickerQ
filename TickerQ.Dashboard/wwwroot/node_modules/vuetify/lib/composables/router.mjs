// Utilities
import { computed, nextTick, onScopeDispose, reactive, resolveDynamicComponent, toRef } from 'vue';
import { deepEqual, getCurrentInstance, hasEvent, IN_BROWSER, propsFactory } from "../util/index.mjs"; // Types
export function useRoute() {
  const vm = getCurrentInstance('useRoute');
  return computed(() => vm?.proxy?.$route);
}
export function useRouter() {
  return getCurrentInstance('useRouter')?.proxy?.$router;
}
export function useLink(props, attrs) {
  const RouterLink = resolveDynamicComponent('RouterLink');
  const isLink = computed(() => !!(props.href || props.to));
  const isClickable = computed(() => {
    return isLink?.value || hasEvent(attrs, 'click') || hasEvent(props, 'click');
  });
  if (typeof RouterLink === 'string' || !('useLink' in RouterLink)) {
    const href = toRef(props, 'href');
    return {
      isLink,
      isClickable,
      href,
      linkProps: reactive({
        href
      })
    };
  }
  // vue-router useLink `to` prop needs to be reactive and useLink will crash if undefined
  const linkProps = computed(() => ({
    ...props,
    to: toRef(() => props.to || '')
  }));
  const routerLink = RouterLink.useLink(linkProps.value);
  // Actual link needs to be undefined when to prop is not used
  const link = computed(() => props.to ? routerLink : undefined);
  const route = useRoute();
  const isActive = computed(() => {
    if (!link.value) return false;
    if (!props.exact) return link.value.isActive?.value ?? false;
    if (!route.value) return link.value.isExactActive?.value ?? false;
    return link.value.isExactActive?.value && deepEqual(link.value.route.value.query, route.value.query);
  });
  const href = computed(() => props.to ? link.value?.route.value.href : props.href);
  return {
    isLink,
    isClickable,
    isActive,
    route: link.value?.route,
    navigate: link.value?.navigate,
    href,
    linkProps: reactive({
      href,
      'aria-current': computed(() => isActive.value ? 'page' : undefined)
    })
  };
}
export const makeRouterProps = propsFactory({
  href: String,
  replace: Boolean,
  to: [String, Object],
  exact: Boolean
}, 'router');
let inTransition = false;
export function useBackButton(router, cb) {
  let popped = false;
  let removeBefore;
  let removeAfter;
  if (IN_BROWSER) {
    nextTick(() => {
      window.addEventListener('popstate', onPopstate);
      removeBefore = router?.beforeEach((to, from, next) => {
        if (!inTransition) {
          setTimeout(() => popped ? cb(next) : next());
        } else {
          popped ? cb(next) : next();
        }
        inTransition = true;
      });
      removeAfter = router?.afterEach(() => {
        inTransition = false;
      });
    });
    onScopeDispose(() => {
      window.removeEventListener('popstate', onPopstate);
      removeBefore?.();
      removeAfter?.();
    });
  }
  function onPopstate(e) {
    if (e.state?.replaced) return;
    popped = true;
    setTimeout(() => popped = false);
  }
}
//# sourceMappingURL=router.mjs.map