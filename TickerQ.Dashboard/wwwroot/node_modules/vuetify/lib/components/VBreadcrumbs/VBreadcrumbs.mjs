import { mergeProps as _mergeProps, Fragment as _Fragment, resolveDirective as _resolveDirective, createVNode as _createVNode } from "vue";
// Styles
import "./VBreadcrumbs.css";

// Components
import { VBreadcrumbsDivider } from "./VBreadcrumbsDivider.mjs";
import { VBreadcrumbsItem } from "./VBreadcrumbsItem.mjs";
import { VDefaultsProvider } from "../VDefaultsProvider/index.mjs";
import { VIcon } from "../VIcon/index.mjs"; // Composables
import { useBackgroundColor } from "../../composables/color.mjs";
import { makeComponentProps } from "../../composables/component.mjs";
import { provideDefaults } from "../../composables/defaults.mjs";
import { makeDensityProps, useDensity } from "../../composables/density.mjs";
import { IconValue } from "../../composables/icons.mjs";
import { makeRoundedProps, useRounded } from "../../composables/rounded.mjs";
import { makeTagProps } from "../../composables/tag.mjs"; // Utilities
import { computed, toRef } from 'vue';
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVBreadcrumbsProps = propsFactory({
  activeClass: String,
  activeColor: String,
  bgColor: String,
  color: String,
  disabled: Boolean,
  divider: {
    type: String,
    default: '/'
  },
  icon: IconValue,
  items: {
    type: Array,
    default: () => []
  },
  ...makeComponentProps(),
  ...makeDensityProps(),
  ...makeRoundedProps(),
  ...makeTagProps({
    tag: 'ul'
  })
}, 'VBreadcrumbs');
export const VBreadcrumbs = genericComponent()({
  name: 'VBreadcrumbs',
  props: makeVBreadcrumbsProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const {
      backgroundColorClasses,
      backgroundColorStyles
    } = useBackgroundColor(toRef(props, 'bgColor'));
    const {
      densityClasses
    } = useDensity(props);
    const {
      roundedClasses
    } = useRounded(props);
    provideDefaults({
      VBreadcrumbsDivider: {
        divider: toRef(props, 'divider')
      },
      VBreadcrumbsItem: {
        activeClass: toRef(props, 'activeClass'),
        activeColor: toRef(props, 'activeColor'),
        color: toRef(props, 'color'),
        disabled: toRef(props, 'disabled')
      }
    });
    const items = computed(() => props.items.map(item => {
      return typeof item === 'string' ? {
        item: {
          title: item
        },
        raw: item
      } : {
        item,
        raw: item
      };
    }));
    useRender(() => {
      const hasPrepend = !!(slots.prepend || props.icon);
      return _createVNode(props.tag, {
        "class": ['v-breadcrumbs', backgroundColorClasses.value, densityClasses.value, roundedClasses.value, props.class],
        "style": [backgroundColorStyles.value, props.style]
      }, {
        default: () => [hasPrepend && _createVNode("li", {
          "key": "prepend",
          "class": "v-breadcrumbs__prepend"
        }, [!slots.prepend ? _createVNode(VIcon, {
          "key": "prepend-icon",
          "start": true,
          "icon": props.icon
        }, null) : _createVNode(VDefaultsProvider, {
          "key": "prepend-defaults",
          "disabled": !props.icon,
          "defaults": {
            VIcon: {
              icon: props.icon,
              start: true
            }
          }
        }, slots.prepend)]), items.value.map((_ref2, index, array) => {
          let {
            item,
            raw
          } = _ref2;
          return _createVNode(_Fragment, null, [slots.item?.({
            item,
            index
          }) ?? _createVNode(VBreadcrumbsItem, _mergeProps({
            "key": index,
            "disabled": index >= array.length - 1
          }, typeof item === 'string' ? {
            title: item
          } : item), {
            default: slots.title ? () => slots.title?.({
              item,
              index
            }) : undefined
          }), index < array.length - 1 && _createVNode(VBreadcrumbsDivider, null, {
            default: slots.divider ? () => slots.divider?.({
              item: raw,
              index
            }) : undefined
          })]);
        }), slots.default?.()]
      });
    });
    return {};
  }
});
//# sourceMappingURL=VBreadcrumbs.mjs.map