// Utilities
import { h, mergeProps, render, resolveComponent } from 'vue';
import { consoleError, isObject } from "../util/index.mjs"; // Types
export function useDirectiveComponent(component, props) {
  const concreteComponent = typeof component === 'string' ? resolveComponent(component) : component;
  const hook = mountComponent(concreteComponent, props);
  return {
    mounted: hook,
    updated: hook,
    unmounted(el) {
      render(null, el);
    }
  };
}
function mountComponent(component, props) {
  return function (el, binding, vnode) {
    const _props = typeof props === 'function' ? props(binding) : props;
    const text = binding.value?.text ?? binding.value ?? _props?.text;
    const value = isObject(binding.value) ? binding.value : {};

    // Get the children from the props or directive value, or the element's children
    const children = () => text ?? el.textContent;

    // If vnode.ctx is the same as the instance, then we're bound to a plain element
    // and need to find the nearest parent component instance to inherit provides from
    const provides = (vnode.ctx === binding.instance.$ ? findComponentParent(vnode, binding.instance.$)?.provides : vnode.ctx?.provides) ?? binding.instance.$.provides;
    const node = h(component, mergeProps(_props, value), children);
    node.appContext = Object.assign(Object.create(null), binding.instance.$.appContext, {
      provides
    });
    render(node, el);
  };
}
function findComponentParent(vnode, root) {
  // Walk the tree from root until we find the child vnode
  const stack = new Set();
  const walk = children => {
    for (const child of children) {
      if (!child) continue;
      if (child === vnode || child.el && vnode.el && child.el === vnode.el) {
        return true;
      }
      stack.add(child);
      let result;
      if (child.suspense) {
        result = walk([child.ssContent]);
      } else if (Array.isArray(child.children)) {
        result = walk(child.children);
      } else if (child.component?.vnode) {
        result = walk([child.component?.subTree]);
      }
      if (result) {
        return result;
      }
      stack.delete(child);
    }
    return false;
  };
  if (!walk([root.subTree])) {
    consoleError('Could not find original vnode, component will not inherit provides');
    return root;
  }

  // Return the first component parent
  const result = Array.from(stack).reverse();
  for (const child of result) {
    if (child.component) {
      return child.component;
    }
  }
  return root;
}
//# sourceMappingURL=directiveComponent.mjs.map