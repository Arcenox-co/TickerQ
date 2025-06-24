// Utilities
import { convertToUnit, destructComputed, getCurrentInstanceName, includes, propsFactory } from "../util/index.mjs"; // Types
const predefinedSizes = ['x-small', 'small', 'default', 'large', 'x-large'];
// Composables
export const makeSizeProps = propsFactory({
  size: {
    type: [String, Number],
    default: 'default'
  }
}, 'size');
export function useSize(props) {
  let name = arguments.length > 1 && arguments[1] !== undefined ? arguments[1] : getCurrentInstanceName();
  return destructComputed(() => {
    let sizeClasses;
    let sizeStyles;
    if (includes(predefinedSizes, props.size)) {
      sizeClasses = `${name}--size-${props.size}`;
    } else if (props.size) {
      sizeStyles = {
        width: convertToUnit(props.size),
        height: convertToUnit(props.size)
      };
    }
    return {
      sizeClasses,
      sizeStyles
    };
  });
}
//# sourceMappingURL=size.mjs.map