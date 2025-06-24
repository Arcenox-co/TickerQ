// Utilities
import { h, mergeProps, Transition, TransitionGroup } from 'vue';
import { propsFactory } from "../util/index.mjs"; // Types
export const makeTransitionProps = propsFactory({
  transition: {
    type: [Boolean, String, Object],
    default: 'fade-transition',
    validator: val => val !== true
  }
}, 'transition');
export const MaybeTransition = (props, _ref) => {
  let {
    slots
  } = _ref;
  const {
    transition,
    disabled,
    group,
    ...rest
  } = props;
  const {
    component = group ? TransitionGroup : Transition,
    ...customProps
  } = typeof transition === 'object' ? transition : {};
  return h(component, mergeProps(typeof transition === 'string' ? {
    name: disabled ? '' : transition
  } : customProps, typeof transition === 'string' ? {} : Object.fromEntries(Object.entries({
    disabled,
    group
  }).filter(_ref2 => {
    let [_, v] = _ref2;
    return v !== undefined;
  })), rest), slots);
};
//# sourceMappingURL=transition.mjs.map