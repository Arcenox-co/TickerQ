import { createVNode as _createVNode, Fragment as _Fragment } from "vue";
// Styles
import "./VEmptyState.css";

// Components
import { VBtn } from "../VBtn/index.mjs";
import { VDefaultsProvider } from "../VDefaultsProvider/index.mjs";
import { VIcon } from "../VIcon/index.mjs";
import { VImg } from "../VImg/index.mjs"; // Composables
import { useBackgroundColor } from "../../composables/color.mjs";
import { makeComponentProps } from "../../composables/component.mjs";
import { makeDimensionProps, useDimension } from "../../composables/dimensions.mjs";
import { useDisplay } from "../../composables/display.mjs";
import { IconValue } from "../../composables/icons.mjs";
import { makeSizeProps } from "../../composables/size.mjs";
import { makeThemeProps, provideTheme } from "../../composables/theme.mjs"; // Utilities
import { toRef } from 'vue';
import { convertToUnit, genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
// Types
export const makeVEmptyStateProps = propsFactory({
  actionText: String,
  bgColor: String,
  color: String,
  icon: IconValue,
  image: String,
  justify: {
    type: String,
    default: 'center'
  },
  headline: String,
  title: String,
  text: String,
  textWidth: {
    type: [Number, String],
    default: 500
  },
  href: String,
  to: String,
  ...makeComponentProps(),
  ...makeDimensionProps(),
  ...makeSizeProps({
    size: undefined
  }),
  ...makeThemeProps()
}, 'VEmptyState');
export const VEmptyState = genericComponent()({
  name: 'VEmptyState',
  props: makeVEmptyStateProps(),
  emits: {
    'click:action': e => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const {
      themeClasses
    } = provideTheme(props);
    const {
      backgroundColorClasses,
      backgroundColorStyles
    } = useBackgroundColor(toRef(props, 'bgColor'));
    const {
      dimensionStyles
    } = useDimension(props);
    const {
      displayClasses
    } = useDisplay();
    function onClickAction(e) {
      emit('click:action', e);
    }
    useRender(() => {
      const hasActions = !!(slots.actions || props.actionText);
      const hasHeadline = !!(slots.headline || props.headline);
      const hasTitle = !!(slots.title || props.title);
      const hasText = !!(slots.text || props.text);
      const hasMedia = !!(slots.media || props.image || props.icon);
      const size = props.size || (props.image ? 200 : 96);
      return _createVNode("div", {
        "class": ['v-empty-state', {
          [`v-empty-state--${props.justify}`]: true
        }, themeClasses.value, backgroundColorClasses.value, displayClasses.value, props.class],
        "style": [backgroundColorStyles.value, dimensionStyles.value, props.style]
      }, [hasMedia && _createVNode("div", {
        "key": "media",
        "class": "v-empty-state__media"
      }, [!slots.media ? _createVNode(_Fragment, null, [props.image ? _createVNode(VImg, {
        "key": "image",
        "src": props.image,
        "height": size
      }, null) : props.icon ? _createVNode(VIcon, {
        "key": "icon",
        "color": props.color,
        "size": size,
        "icon": props.icon
      }, null) : undefined]) : _createVNode(VDefaultsProvider, {
        "key": "media-defaults",
        "defaults": {
          VImg: {
            src: props.image,
            height: size
          },
          VIcon: {
            size,
            icon: props.icon
          }
        }
      }, {
        default: () => [slots.media()]
      })]), hasHeadline && _createVNode("div", {
        "key": "headline",
        "class": "v-empty-state__headline"
      }, [slots.headline?.() ?? props.headline]), hasTitle && _createVNode("div", {
        "key": "title",
        "class": "v-empty-state__title"
      }, [slots.title?.() ?? props.title]), hasText && _createVNode("div", {
        "key": "text",
        "class": "v-empty-state__text",
        "style": {
          maxWidth: convertToUnit(props.textWidth)
        }
      }, [slots.text?.() ?? props.text]), slots.default && _createVNode("div", {
        "key": "content",
        "class": "v-empty-state__content"
      }, [slots.default()]), hasActions && _createVNode("div", {
        "key": "actions",
        "class": "v-empty-state__actions"
      }, [_createVNode(VDefaultsProvider, {
        "defaults": {
          VBtn: {
            class: 'v-empty-state__action-btn',
            color: props.color ?? 'surface-variant',
            text: props.actionText
          }
        }
      }, {
        default: () => [slots.actions?.({
          props: {
            onClick: onClickAction
          }
        }) ?? _createVNode(VBtn, {
          "onClick": onClickAction
        }, null)]
      })])]);
    });
    return {};
  }
});
//# sourceMappingURL=VEmptyState.mjs.map