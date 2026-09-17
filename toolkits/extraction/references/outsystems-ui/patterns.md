# OutSystems UI Pattern Reference (O11)

> Generated 2026-09-17 by `scripts/Build-UiReference.ps1` from the ground-of-truth repo's
> `outsystems-ui` source (src/scss + src/scripts/OutSystems/OSUI/Patterns). DO NOT EDIT BY HAND.
> Source repo: `C:\Users\Ricardo Figueiredo\Documents\Outsystems_Custom\references\repos`

Use this catalog to identify OutSystems UI patterns in extracted modules: web block names,
CSS classes (`osui-*`), and CSS custom properties (`--osui-*`) map to these patterns.
The base stylesheet every O11 Reactive theme extends is `classic-theme-o11.css` (same folder).

| | Count |
|---|---|
| JS-driven patterns (public JS API) | 33 |
| CSS-only patterns | 37 |

---

## JS-driven patterns

### Accordion

- CSS class root: `.osui-accordion` | Category: 02-content
- Classes: `.list`, `.osui-accordion`, `.osui-accordion-item`, `.osui-accordion-item__title`, `.osui-accordion-item--is-open`
- CSS variables (CSS API): `--osui-accordion-border-radius`
- JS API (`OutSystems.OSUI.Patterns.AccordionAPI`): ChangeProperty, CollapseAllItems, Create, Dispose, ExpandAllItems, GetAccordionById, GetAllAccordions, Initialize, RegisterCallback

### AccordionItem

- CSS class root: `.osui-accordion-item` | Category: 02-content
- Classes: `.animated-label`, `.choices__list`, `.choices__list--dropdown`, `.dropdown-list`, `.has-accessible-features`, `.iconLibrary-phosphor`, `.layout-native`, `.osui-accordion`, `.osui-accordion-item`, `.osui-accordion-item__click_zone`, `.osui-accordion-item__icon`, `.osui-accordion-item__title`, `.osui-accordion-item--is-open`, `.phone`
- CSS variables (CSS API): `--accordion-active-border-size`, `--custom`, `--osui-accordion-item-active-indicator-color`, `--osui-accordion-item-background`, `--osui-accordion-item-border-color`, `--osui-accordion-item-border-radius`, `--osui-accordion-item-border-width`, `--osui-accordion-item-color`, `--osui-accordion-item-icon-color`, `--osui-accordion-item-plus-minus-size`, `--osui-accordion-item-title-hover-background`, `--osui-accordion-item-title-hover-border-color`, `--plus-minus`
- JS API (`OutSystems.OSUI.Patterns.AccordionItemAPI`): AllowTitleEvents, ChangeProperty, Collapse, Create, Dispose, Expand, GetAccordionItemById, GetAllAccordionItems, Initialize, RegisterCallback, ToggleClickableZone

### AnimatedLabel

- CSS class root: `.osui-animated-label` | Category: 03-interaction
- Classes: `.active`, `.animated-label`, `.animated-label-input`, `.animated-label-text`, `.form`, `.form-control`, `.has-accessible-features`, `.icon`, `.input-text`, `.input-with-icon`, `.input-with-icon-content-icon`, `.input-with-icon-input`, `.input-with-icon-right`, `.list`, `.list-group`, `.not-valid`, `.os-high-contrast`, `.phone`, `.tablet`, `.validation-message`
- CSS variables (CSS API): `--osui-animated-label-border-color`, `--osui-animated-label-color`, `--osui-animated-label-disabled-background`, `--osui-animated-label-disabled-color`, `--osui-animated-label-error-color`, `--osui-animated-label-focus-border-color`, `--osui-animated-label-focus-ring-color`, `--osui-animated-label-focus-shadow`, `--osui-animated-label-hover-border-color`
- JS API (`OutSystems.OSUI.Patterns.AnimatedLabelAPI`): ChangeProperty, Create, Dispose, GetAllAnimatedLabels, GetAnimatedLabelById, Initialize, RegisterCallback, UpdateOnRender

### BottomSheet

- CSS class root: `.osui-bottom-sheet` | Category: 01-adaptive
- Classes: `.desktop`, `.landscape`, `.layout`, `.layout-native`, `.os-high-contrast`, `.osui-bottom-sheet`, `.osui-bottom-sheet__content`, `.osui-bottom-sheet__header`, `.osui-bottom-sheet__header__top-bar`, `.osui-bottom-sheet--is-active`, `.osui-bottom-sheet--is-open`, `.osui-bottom-sheet-overlay`, `.osui-datepicker`, `.osui-monthpicker`, `.osui-timepicker`, `.vscomp-wrapper`
- CSS variables (CSS API): `--border-radius-rounded`, `--border-radius-sharp`, `--bottom-sheet-max-height`, `--osui-bottom-sheet-background`, `--osui-bottom-sheet-draggable-area`, `--osui-bottom-sheet-handler-background`, `--osui-bottom-sheet-handler-width`, `--osui-bottom-sheet-padding`, `--osui-bottom-sheet-shadow`, `--osui-bottom-sheet-transition-function`
- JS API (`OutSystems.OSUI.Patterns.BottomSheetAPI`): ChangeProperty, Close, Create, Dispose, GetAllBottomSheetItemsMap, GetBottomSheetItemById, Initialize, Open, RegisterCallback

### ButtonLoading

- CSS class root: `.osui-button-loading` | Category: 03-interaction
- Classes: `.btn`, `.os-high-contrast`, `.osui-btn-loading`, `.osui-btn-loading__spinner-animation`, `.osui-btn-loading--is-loading`, `.phone`
- JS API (`OutSystems.OSUI.Patterns.ButtonLoadingAPI`): ChangeProperty, Create, Dispose, ForceIsLoadingDisabledState, GetAllButtonsLoading, GetButtonLoadingById, Initialize, RegisterCallback

### Carousel

- CSS class root: `.osui-carousel` | Category: 02-content | Provider: Splide 4.1.3
- Classes: `.has-accessible-features`, `.is-active`, `.is-rtl`, `.list`, `.os-high-contrast`, `.osui-carousel`, `.splide`, `.splide__arrow`, `.splide__arrow--next`, `.splide__list`, `.splide__pagination`, `.splide__slide`, `.splide__track--fade`, `.splide--draggable`
- CSS variables (CSS API): `--osui-carousel-arrow-background`, `--osui-carousel-arrow-border-radius`, `--osui-carousel-arrow-disabled-overlay`, `--osui-carousel-arrow-icon-color`, `--osui-carousel-arrow-shadow`, `--osui-carousel-pagination-active-color`, `--osui-carousel-pagination-color`, `--osui-carousel-pagination-margin`, `--osui-carousel-track-width`
- JS API (`OutSystems.OSUI.Patterns.CarouselAPI`): CarouselDisableOnRender, CarouselEnableOnRender, ChangeProperty, Create, Dispose, GetAllCarouselItemsMap, GetCarouselItemById, GoTo, Initialize, Next, Previous, RegisterCallback, SetCarouselDirection, SetProviderConfigs, SetProviderEvent, ToggleDrag, UnsetProviderEvent, UpdateOnRender

### DatePicker

- CSS class root: `.osui-date-picker` | Category: 03-interaction | Provider: Flatpickr 4.6.13
- Classes: `.flatpickr-calendar`, `.flatpickr-input`, `.flatpickr-mobile`, `.form`, `.has-today-btn`, `.inline`, `.input`, `.not-valid`, `.osui-datepicker`, `.osui-datepicker-calendar`, `.osui-datepicker-calendar-ss-preview`, `.osui-datepicker-calendar-ss-preview--time`, `.osui-datepicker-calendar-ss-preview--today`, `.placeholder-ss-preview`, `.time12h`, `.time24h`, `.validation-message`
- CSS variables (CSS API): `--osui-datepicker-disabled-background`, `--osui-datepicker-disabled-border-color`, `--osui-datepicker-disabled-color`, `--osui-datepicker-input-border-radius`, `--osui-input-border-radius`, `--time`
- JS API (`OutSystems.OSUI.Patterns.DatePickerAPI`): ChangeProperty, Clear, Close, Create, DisableDays, DisableWeekDays, Dispose, GetAllDatePickerItemsMap, GetDatePickerItemById, Initialize, OnRender, Open, Redraw, RegisterCallback, SetEditableInput, SetLanguage, SetProviderConfigs, SetProviderEvent, ToggleNativeBehavior, UnsetProviderEvent, UpdateInitialDate, UpdatePrompt

### Dropdown

- CSS class root: `.osui-dropdown` | Category: 03-interaction | Provider: VirtualSelect 1.4.0
- Classes: `.android`, `.bold`, `.form`, `.form-control`, `.has-accessible-features`, `.has-value`, `.ios`, `.landscape`, `.Offset`, `.os-high-contrast`, `.osui-balloon`, `.osui-dropdown`, `.osui-dropdown--not-valid`, `.osui-dropdown-search`, `.osui-dropdown-serverside`, `.osui-dropdown-serverside__balloon`, `.osui-dropdown-serverside__balloon-container`, `.osui-dropdown-serverside__balloon-content`, `.osui-dropdown-serverside__balloon-search-icon`, `.osui-dropdown-serverside__balloon-search-wrapper`, `.osui-dropdown-serverside__search-input--is-focused`, `.osui-dropdown-serverside__selected-values`, `.osui-dropdown-serverside__selected-values-wrapper`, `.osui-dropdown-serverside__text`, `.osui-dropdown-serverside-error-message`, `.osui-dropdown-serverside--is-disabled`, `.osui-dropdown-serverside--is-opened`, `.osui-dropdown-serverside-item`, `.osui-dropdown-serverside-item--is-selected`, `.osui-dropdown-tags`, `.osui-tabs`, `.phone`, `.portrait`, `.tablet`, `.ts`, `.vscomp`, `.vscomp-ele`, `.vscomp-toggle-button`, `.vscomp-value`, `.vscomp-wrapper`, `.wcag-hide-text`, `.windows`
- CSS variables (CSS API): `--ballon-top-margin-value`, `--osui-balloon-border-radius`, `--osui-balloon-shadow`, `--osui-dropdown-arrow-color`, `--osui-dropdown-arrow-size`, `--osui-dropdown-background`, `--osui-dropdown-border-color`, `--osui-dropdown-border-radius`, `--osui-dropdown-disabled-background`, `--osui-dropdown-disabled-border-color`, `--osui-dropdown-disabled-color`, `--osui-dropdown-focus-border-color`, `--osui-dropdown-focus-ring-color`, `--osui-dropdown-hover-border-color`, `--osui-dropdown-item-background`, `--osui-dropdown-item-color`, `--osui-dropdown-item-hover-bg`, `--osui-dropdown-list-background`, `--osui-dropdown-ss-arrow-color`, `--osui-dropdown-ss-arrow-size`, `--osui-dropdown-ss-background`, `--osui-dropdown-ss-balloon-max-height`, `--osui-dropdown-ss-border-color`, `--osui-dropdown-ss-border-radius`, `--osui-dropdown-ss-color`, `--osui-dropdown-ss-disabled-background`, `--osui-dropdown-ss-disabled-color`, `--osui-dropdown-ss-focus-border-color`, `--osui-dropdown-ss-focus-ring-color`, `--osui-dropdown-ss-hover-border-color`, `--osui-dropdown-ss-min-width`, `--osui-dropdown-ss-popup-border-radius`, `--osui-dropdown-ss-prompt-color`, `--osui-dropdown-ss-scroll-bar-width`, `--osui-dropdown-tag-color`, `--osui-floating-offset`, `--osui-outline-size`
- JS API (`OutSystems.OSUI.Patterns.DropdownAPI`): ChangeProperty, Clear, Close, Create, Disable, Dispose, Enable, GetAllDropdowns, GetDropdownById, GetSelectedValues, Initialize, Open, RegisterCallback, SetProviderConfigs, SetProviderEvent, SetValidation, SetValues, TogglePopup, UnsetProviderEvent

### DropdownServerSideItem

- CSS class root: `.osui-dropdown-server-side-item` | Category: 03-interaction
- SCSS: CSS shared with the dropdown pattern (no dedicated SCSS)
- JS API (`OutSystems.OSUI.Patterns.DropdownServerSideItemAPI`): ChangeProperty, Create, Dispose, GetAllDropdownServerSideItemItemsMap, GetDropdownServerSideItemItemById, Initialize, RegisterCallback

### FlipContent

- CSS class root: `.osui-flip-content` | Category: 02-content
- Classes: `.firefox`, `.is-rtl`, `.osui-flip-content`, `.osui-flip-content__container`
- CSS variables (CSS API): `--webkit-perspective`, `--webkit-transform-style`
- JS API (`OutSystems.OSUI.Patterns.FlipContentAPI`): ChangeProperty, Create, Dispose, GetAllFlipContent, GetFlipContentById, Initialize, RegisterCallback, ShowBackContent, ShowFrontContent, ToggleFlipContent

### Gallery

- CSS class root: `.osui-gallery` | Category: 01-adaptive
- Classes: `.animate`, `.card`, `.card-background`, `.list`, `.osui-gallery`, `.phone`, `.tablet`
- JS API (`OutSystems.OSUI.Patterns.GalleryAPI`): ChangeProperty, Create, Dispose, GetAllGalleries, GetGalleryById, Initialize, RegisterCallback

### InlineSvg

- CSS class root: `.osui-inline-svg` | Category: -
- SCSS: no dedicated SCSS (logic/behavior only)
- JS API (`OutSystems.OSUI.Patterns.InlineSvgAPI`): ChangeProperty, Create, Dispose, GetAllInlineSvgs, GetInlineSvgById, Initialize, RegisterCallback, SetAccessibilityProperties

### MonthPicker

- CSS class root: `.osui-month-picker` | Category: 03-interaction | Provider: Flatpickr 4.6.13
- Classes: `.flatpickr-calendar`, `.flatpickr-mobile`, `.form`, `.inline`, `.input`, `.not-valid`, `.osui-monthpicker`, `.osui-monthpicker-ss-preview`, `.placeholder-ss-preview`
- CSS variables (CSS API): `--osui-input-border-radius`, `--osui-monthpicker-disabled-background`, `--osui-monthpicker-disabled-border-color`, `--osui-monthpicker-disabled-color`, `--osui-monthpicker-input-border-radius`
- JS API (`OutSystems.OSUI.Patterns.MonthPickerAPI`): ChangeProperty, Clear, Close, Create, Dispose, GetAllMonthPickerItemsMap, GetMonthPickerItemById, Initialize, OnRender, Open, RegisterCallback, SetEditableInput, SetLanguage, SetProviderConfigs, SetProviderEvent, UnsetProviderEvent, UpdateInitialMonth, UpdatePrompt

### Notification

- CSS class root: `.osui-notification` | Category: 03-interaction
- Classes: `.android`, `.bottom`, `.bottom-left`, `.bottom-right`, `.center`, `.fade-enter`, `.fade-leave`, `.is-bottom`, `.is-bottom-left`, `.is-bottom-right`, `.is-center`, `.is-left`, `.is-right`, `.is-top`, `.is-top-left`, `.is-top-right`, `.layout-native`, `.left`, `.osui-notification`, `.osui-notification--is-open`, `.phone`, `.right`, `.slide-from-bottom-enter`, `.slide-from-bottom-leave`, `.slide-from-left-enter`, `.slide-from-left-leave`, `.slide-from-right-enter`, `.slide-from-right-leave`, `.slide-from-top-enter`, `.slide-from-top-leave`, `.top`, `.top-left`, `.top-right`
- CSS variables (CSS API): `--osui-notification-background`, `--osui-notification-border-radius`, `--osui-notification-color`, `--osui-notification-gap`, `--osui-notification-margin`, `--osui-notification-padding`, `--osui-notification-shadow`
- JS API (`OutSystems.OSUI.Patterns.NotificationAPI`): ChangeProperty, Create, Dispose, GetAllNotifications, GetNotificationById, Hide, Initialize, RegisterCallback, Show

### OverflowMenu

- CSS class root: `.osui-overflow-menu` | Category: 03-interaction
- Classes: `.btn`, `.desktop`, `.os-high-contrast`, `.osui-balloon`, `.osui-overflow-menu`, `.osui-overflow-menu__balloon`, `.osui-overflow-menu__trigger`, `.popup-backdrop`
- CSS variables (CSS API): `--border-radius-rounded`, `--border-radius-soft`, `--osui-balloon-background`, `--osui-balloon-border-radius`, `--osui-balloon-shadow`, `--osui-btn-active-background`, `--osui-btn-background`, `--osui-btn-height`, `--osui-btn-hover-background`, `--osui-floating-offset`, `--osui-overflow-menu-background`, `--osui-overflow-menu-border-radius`, `--osui-overflow-menu-color`, `--osui-overflow-menu-min-width`, `--osui-overflow-menu-shadow`, `--osui-overflow-menu-trigger-active-bg`
- JS API (`OutSystems.OSUI.Patterns.OverflowMenuAPI`): ChangeProperty, Close, Create, Disable, Dispose, Enable, GetAllOverflowMenus, GetOverflowMenuById, Initialize, Open, RegisterCallback

### Progress

- CSS class root: `.osui-progress` | Category: 05-numbers
- Classes: `.animate`, `.is-rtl`, `.os-high-contrast`, `.osui-inline-svg`, `.osui-progress`, `.osui-progress-bar__value`, `.osui-progress-circle`, `.svg-wrapper`
- CSS variables (CSS API): `--progress-value`
- JS API (`OutSystems.OSUI.Patterns.ProgressAPI`): ChangeProperty, Create, Dispose, GetAllProgressItemsMap, GetProgressItemById, Initialize, ProgressApplyGradient, RegisterCallback, ResetProgressValue, SetProgressValue

### RangeSlider

- CSS class root: `.osui-range-slider` | Category: 03-interaction | Provider: noUiSlider 15.8.1
- Classes: `.has-accessible-features`, `.is-rtl`, `.noUi`, `.noUi-active`, `.noUi-base`, `.noUi-connect`, `.noUi-connects`, `.noUi-handle`, `.noUi-horizontal`, `.noUi-marker`, `.noUi-marker-large`, `.noUi-marker-vertical`, `.noUi-origin`, `.noUi-pips`, `.noUi-pips-margin`, `.noUi-rtl`, `.noUi-target`, `.noUi-tooltip`, `.noUi-txt-dir-rtl`, `.noUi-value`, `.noUi-value-vertical`, `.noUi-vertical`, `.os-high-contrast`, `.osui-range-slider`, `.osui-range-slider__provider`, `.osui-range-slider--has-ticks`
- CSS variables (CSS API): `--osui-range-slider-disabled-color`, `--osui-range-slider-disabled-connect-color`, `--osui-range-slider-disabled-track-color`, `--osui-range-slider-handle-background`, `--osui-range-slider-handle-border-color`, `--osui-range-slider-handle-shadow`, `--osui-range-slider-track-color`, `--osui-range-slider-track-radius`, `--range-slider-handle-size`, `--range-slider-handle-size-half`, `--range-slider-handle-sliding-position`, `--range-slider-thickness`, `--range-slider-thickness-half`
- JS API (`OutSystems.OSUI.Patterns.RangeSliderAPI`): ChangeProperty, Create, Disable, Dispose, Enable, GetAllRangeSliderItemsMap, GetRangeSliderItemById, Initialize, RegisterCallback, ResetRangeSliderValue, SetProviderConfigs, SetProviderEvent, SetRangeIntervalChangeOnDragEnd, SetRangeSliderValue, UnsetProviderEvent

### Rating

- CSS class root: `.osui-rating` | Category: 05-numbers
- Classes: `.chrome`, `.com`, `.edge`, `.form`, `.has-accessible-features`, `.icon`, `.icon-states`, `.ios`, `.is-edit`, `.is-half`, `.is-rtl`, `.os-high-contrast`, `.osui-tabs`, `.rating`, `.rating-item`, `.rating-item-empty`, `.rating-item-filled`, `.rating-item-half`, `.rating-medium`, `.rating-small`, `.text-neutral-5`, `.text-primary`, `.wcag-hide-text`
- CSS variables (CSS API): `--osui-rating-disabled-color`, `--osui-rating-disabled-empty-color`, `--osui-rating-empty-color`, `--osui-rating-filled-color`, `--rating-size`
- JS API (`OutSystems.OSUI.Patterns.RatingAPI`): ChangeProperty, Create, Disable, Dispose, Enable, GetAllRatings, GetRatingById, Initialize, RegisterCallback

### Search

- CSS class root: `.osui-search` | Category: 03-interaction
- Classes: `.fade-enter`, `.fade-leave`, `.form`, `.form-control`, `.header`, `.header-content`, `.header-right`, `.layout-native`, `.osui-search`, `.osui-search__input`, `.slide-from-bottom-enter`, `.slide-from-bottom-leave`, `.slide-from-left-enter`, `.slide-from-left-leave`, `.slide-from-right-enter`, `.slide-from-right-leave`, `.slide-from-top-enter`, `.slide-from-top-leave`
- JS API (`OutSystems.OSUI.Patterns.SearchAPI`): ChangeProperty, Create, Dispose, GetAllSearches, GetSearchById, Initialize, RegisterCallback

### SectionIndex

- CSS class root: `.osui-section-index` | Category: 04-navigation
- Classes: `.has-accessible-features`, `.is-rtl`, `.os-high-contrast`, `.osui-section-index`, `.osui-section-index--is-sticky`, `.osui-section-index-item`, `.phone`, `.safari`
- CSS variables (CSS API): `--osui-section-index-active-indicator-color`, `--osui-section-index-active-indicator-offset`, `--osui-section-index-active-indicator-width`, `--osui-section-index-border-color`, `--osui-section-index-border-width`, `--osui-section-index-item-active-color`, `--osui-section-index-item-border-radius`, `--osui-section-index-item-color`, `--osui-section-index-item-color-hover`, `--osui-section-index-item-hover-background`, `--osui-section-index-item-press-background`, `--top-position`
- JS API (`OutSystems.OSUI.Patterns.SectionIndexAPI`): ChangeProperty, Create, Dispose, GetAllSectionIndexItemsMap, GetSectionIndexById, Initialize, RegisterCallback

### SectionIndexItem

- CSS class root: `.osui-section-index-item` | Category: 04-navigation
- SCSS: CSS shared with the section-index pattern (no dedicated SCSS)
- JS API (`OutSystems.OSUI.Patterns.SectionIndexItemAPI`): ChangeProperty, Create, Dispose, GetAllSectionIndexItemItemsMap, GetSectionIndexItemById, Initialize, RegisterCallback

### Sidebar

- CSS class root: `.osui-sidebar` | Category: 04-navigation
- Classes: `.active-screen`, `.android`, `.desktop`, `.ios`, `.landscape`, `.layout-native`, `.no-transition`, `.os-high-contrast`, `.osui-sidebar`, `.osui-sidebar--has-overlay`, `.osui-sidebar--is-open`, `.phone`
- CSS variables (CSS API): `--osui-sidebar-background`, `--osui-sidebar-color`, `--osui-sidebar-padding-block`, `--osui-sidebar-padding-inline`, `--osui-sidebar-shadow`, `--overlay-opacity`
- JS API (`OutSystems.OSUI.Patterns.SidebarAPI`): ChangeProperty, ClickOutsideToClose, Close, Create, Dispose, GetAllSidebars, GetSidebarById, Initialize, Open, RegisterCallback, ToggleGestures

### Submenu

- CSS class root: `.osui-submenu` | Category: 04-navigation
- Classes: `.active`, `.app-menu-links`, `.columns`, `.columns-item`, `.desktop`, `.e`, `.has-accessible-features`, `.header`, `.header-navigation`, `.is-disabled`, `.is-rtl`, `.layout`, `.layout-side`, `.menu-visible`, `.os-high-contrast`, `.osui-submenu`, `.osui-submenu__header`, `.osui-submenu__header__icon`, `.osui-submenu__header__item`, `.osui-submenu__items`, `.osui-submenu--is-dropdown`, `.osui-submenu--is-open`, `.phone`, `.tablet`
- CSS variables (CSS API): `--osui-submenu-active-border-color`, `--osui-submenu-header-color`, `--osui-submenu-item-border-radius`, `--osui-submenu-item-color`, `--osui-submenu-item-color-disabled`, `--osui-submenu-item-font-size`, `--osui-submenu-item-line-height`, `--osui-submenu-item-pressed-background`, `--osui-submenu-items-background`, `--osui-submenu-items-border-color`, `--osui-submenu-items-border-radius`, `--osui-submenu-item-selected-background`, `--osui-submenu-items-shadow`, `--osui-submenu-max-height`
- JS API (`OutSystems.OSUI.Patterns.SubmenuAPI`): ChangeProperty, ClickOutsideToClose, Close, Create, Dispose, GetAllSubmenus, GetSubmenuById, Initialize, Open, RegisterCallback, SubmenuOpenOnHover, UpdateOnRender

### SwipeEvents

- CSS class root: `.osui-swipe-events` | Category: -
- SCSS: no dedicated SCSS (logic/behavior only)
- JS API (`OutSystems.OSUI.Patterns.SwipeEventsAPI`): Create, Dispose, GestureEnd, GestureMove, GetAllSwipeEvents, GetSwipeEventsById, Initialize, RegisterCallback

### Tabs

- CSS class root: `.osui-tabs` | Category: 04-navigation
- Classes: `.chrome`, `.columns`, `.display-contents`, `.edge`, `.has-accessible-features`, `.is-rtl`, `.os-high-contrast`, `.osui-accordion`, `.osui-tabs`, `.osui-tabs__content`, `.osui-tabs__content-item`, `.osui-tabs__header`, `.osui-tabs__header__indicator`, `.osui-tabs__header-item`, `.osui-tabs__preview`, `.osui-tabs__preview--is-active`, `.osui-tabs--has-drag`, `.osui-tabs--is-active`, `.osui-tabs--is-horizontal`, `.osui-tabs--is-left`, `.osui-tabs--is-right`, `.osui-tabs--is-vertical`, `.osx`, `.section-expandable`, `.uieditor-if-branch-widget`, `.windows`
- CSS variables (CSS API): `--has-drag`, `--header-item-alignment`, `--header-item-width`, `--is-active`, `--osui-tabs-border-color`, `--osui-tabs-header-gap`, `--osui-tabs-header-item-border-radius`, `--osui-tabs-header-item-color`, `--osui-tabs-header-item-color-active`, `--osui-tabs-header-item-color-active-hover`, `--osui-tabs-header-item-color-disabled`, `--osui-tabs-header-item-color-hover`, `--osui-tabs-header-item-font-size`, `--osui-tabs-header-item-hover-background`, `--osui-tabs-header-item-line-height`, `--osui-tabs-indicator-color`, `--tabs-indicator-size`
- JS API (`OutSystems.OSUI.Patterns.TabsAPI`): ChangeProperty, Create, Dispose, GetAllTabs, GetTabsById, Initialize, RegisterCallback, SetActiveTab, TabsToggleSwipe

### TabsContentItem

- CSS class root: `.osui-tabs-content-item` | Category: 04-navigation
- SCSS: CSS shared with the tabs pattern (no dedicated SCSS)
- JS API (`OutSystems.OSUI.Patterns.TabsContentItemAPI`): ChangeProperty, Create, Dispose, GetAllTabsContentItems, GetTabsContentItemById, Initialize, RegisterCallback

### TabsHeaderItem

- CSS class root: `.osui-tabs-header-item` | Category: 04-navigation
- SCSS: CSS shared with the tabs pattern (no dedicated SCSS)
- JS API (`OutSystems.OSUI.Patterns.TabsHeaderItemAPI`): ChangeProperty, Create, DisableTabItem, Dispose, EnableTabItem, GetAllTabsHeaderItems, GetTabsHeaderItemById, Initialize, RegisterCallback, UpdateOnRender

### TimePicker

- CSS class root: `.osui-time-picker` | Category: 03-interaction | Provider: Flatpickr 4.6.13
- Classes: `.dropdown--is-large`, `.dropdown--is-small`, `.flatpickr-am-pm`, `.flatpickr-input`, `.flatpickr-mobile`, `.flatpickr-time`, `.flatpickr-time-separator`, `.form`, `.hasTime`, `.input`, `.noCalendar`, `.not-valid`, `.numInputWrapper`, `.osui-timepicker`, `.osui-timepicker__dropdown`, `.osui-timepicker__dropdown-ss-preview`, `.placeholder-ss-preview`, `.time12h`, `.time24hr`, `.validation-message`
- CSS variables (CSS API): `--osui-input-border-radius`, `--osui-timepicker-disabled-background`, `--osui-timepicker-disabled-border-color`, `--osui-timepicker-disabled-color`, `--osui-timepicker-input-border-radius`
- JS API (`OutSystems.OSUI.Patterns.TimePickerAPI`): ChangeProperty, Clear, Close, Create, Dispose, GetAllTimePickerItemsMap, GetTimePickerItemById, Initialize, OnRender, Open, Redraw, RegisterCallback, SetEditableInput, SetLanguage, SetProviderConfigs, SetProviderEvent, ToggleNativeBehavior, UnsetProviderEvent, UpdateInitialTime, UpdatePrompt

### Tooltip

- CSS class root: `.osui-tooltip` | Category: 03-interaction
- Classes: `.os-high-contrast`, `.osui-balloon`, `.osui-balloon--is-open`, `.osui-tooltip`, `.osui-tooltip__balloon-wrapper`
- CSS variables (CSS API): `--osui-floating-offset`, `--osui-tooltip-arrow-size`, `--osui-tooltip-background`, `--osui-tooltip-background-color`, `--osui-tooltip-border-radius`, `--osui-tooltip-color`, `--osui-tooltip-font-size`, `--osui-tooltip-max-width`, `--osui-tooltip-min-height`, `--osui-tooltip-padding`, `--osui-tooltip-shadow`
- JS API (`OutSystems.OSUI.Patterns.TooltipAPI`): ChangeProperty, Close, Create, Dispose, GetAllTooltips, GetTooltipById, Initialize, Open, RegisterCallback

### TouchEvents

- CSS class root: `.osui-touch-events` | Category: -
- SCSS: no dedicated SCSS (logic/behavior only)
- JS API (`OutSystems.OSUI.Patterns.TouchEventsAPI`): Create, Dispose, GetAllTouchEvents, GetTouchEventsById, Initialize, RegisterCallback

### Video

- CSS class root: `.osui-video` | Category: 02-content
- Classes: `.osui-video`
- JS API (`OutSystems.OSUI.Patterns.VideoAPI`): ChangeProperty, Create, Dispose, GetAllVideos, GetState, GetVideoById, Initialize, JumpToTime, Pause, Play, RegisterCallback

### Wizard

- CSS class root: `.osui-wizard` | Category: 02-content
- Classes: `.icon`, `.is-active`, `.is-horizontal`, `.is-interactive`, `.is-next`, `.is-past`, `.is-reversed`, `.is-vertical`, `.list`, `.OSBlockWidget`, `.osui-wizard`, `.osui-wizard-item`, `.osui-wizard-item-icon`, `.osui-wizard-item-icon-wrapper`, `.osui-wizard-item-label`
- CSS variables (CSS API): `--osui-wizard-active-color`, `--osui-wizard-active-icon-color`, `--osui-wizard-connector-color`, `--osui-wizard-connector-radius`, `--osui-wizard-connector-size`, `--osui-wizard-focus-halo-color`, `--osui-wizard-focus-halo-width`, `--osui-wizard-font-size`, `--osui-wizard-icon-background`, `--osui-wizard-icon-border-color`, `--osui-wizard-icon-border-width`, `--osui-wizard-icon-color`, `--osui-wizard-icon-font-weight`, `--osui-wizard-icon-glyph-size`, `--osui-wizard-icon-padding`, `--osui-wizard-icon-size`, `--osui-wizard-item-spacing`, `--osui-wizard-label-color`, `--osui-wizard-label-line-height`, `--osui-wizard-next-icon-color`, `--osui-wizard-past-background`, `--osui-wizard-past-color`
- JS API (`OutSystems.OSUI.Patterns.WizardAPI`): ChangeProperty, Create, Dispose, GetAllWizards, GetWizardById, Initialize, RegisterCallback

### WizardItem

- CSS class root: `.osui-wizard-item` | Category: 02-content
- SCSS: CSS shared with the wizard pattern (no dedicated SCSS)
- JS API (`OutSystems.OSUI.Patterns.WizardItemAPI`): ChangeProperty, Create, Dispose, GetAllWizardItems, GetWizardItemById, Initialize, RegisterCallback

---

## CSS-only patterns

### action-sheet

- Category: 03-interaction
- Classes: `.action-sheet`, `.action-sheet-actions`, `.action-sheet-buttons`, `.action-sheet-container`, `.action-sheet-container--animatable`, `.action-sheet-container--visible`, `.btn`, `.desktop`, `.os-high-contrast`, `.tablet`
- CSS variables (CSS API): `--osui-action-sheet-actions-layer`, `--osui-action-sheet-background`, `--osui-action-sheet-border-radius`, `--osui-action-sheet-cancel-color`, `--osui-action-sheet-color`, `--osui-action-sheet-divider-color`, `--osui-action-sheet-overlay-background`, `--osui-action-sheet-shadow`, `--osui-btn-active-background`, `--osui-btn-background`, `--osui-btn-border-radius`, `--osui-btn-color`, `--osui-btn-height`, `--osui-btn-hover-background`, `--webkit-transform`

### alert

- Category: 02-content
- Classes: `.alert`, `.alert-icon`, `.alert-message`, `.fa-fw`, `.vivid`
- CSS variables (CSS API): `--osui-alert-accent-color`, `--osui-alert-background`, `--osui-alert-border-radius`, `--osui-alert-color`, `--osui-alert-icon-color`, `--osui-alert-padding`

### align-center

- Category: 06-utilities
- Classes: `.input-text`, `.vertical-align`

### animate

- Category: 03-interaction
- Classes: `.animate`, `.bottom-to-top`, `.bounce`, `.fade-in`, `.fast`, `.flatpickr-calendar`, `.left-to-right`, `.right-to-left`, `.scale`, `.scale-down`, `.scale-up`, `.slow`, `.spinner`, `.top-to-bottom`

### badge

- Category: 05-numbers
- Classes: `.background`, `.background-`, `.background-danger-lightest`, `.background-info-lightest`, `.background-primary-lightest`, `.background-secondary-lightest`, `.background-success-lightest`, `.background-transparent-lightest`, `.background-warning-lightest`, `.badge`, `.get`
- CSS variables (CSS API): `--osui-badge-color`, `--osui-badge-on-light-color`

### balloon

- Category: 03-interaction
- Classes: `.bottom`, `.bottom-end`, `.bottom-left`, `.bottom-right`, `.bottom-start`, `.center`, `.left`, `.left-end`, `.left-start`, `.osui-balloon`, `.osui-balloon--is-open`, `.right`, `.right-end`, `.right-start`, `.top`, `.top-end`, `.top-left`, `.top-right`, `.top-start`
- CSS variables (CSS API): `--border-radius-rounded`, `--border-radius-soft`, `--osui-balloon-background`, `--osui-balloon-border-radius`, `--osui-balloon-position`, `--osui-balloon-shadow`, `--osui-balloon-shape`, `--osui-balloon-width`, `--osui-floating-position-x`, `--osui-floating-position-y`

### blank-slate

- Category: 02-content
- Classes: `.blank-slate`, `.blank-slate-actions`, `.blank-slate-icon`, `.large`
- CSS variables (CSS API): `--osui-blank-slate-description-color`, `--osui-blank-slate-icon-color`

### bottom-bar-item

- Category: 04-navigation
- Classes: `.active`, `.android`, `.bottom-bar`, `.bottom-bar-item-icon`, `.bottom-bar-wrapper`, `.footer`, `.has-accessible-features`, `.layout-native`
- CSS variables (CSS API): `--osui-bottom-bar-background`, `--osui-bottom-bar-border-color`, `--osui-bottom-bar-item-active-color`, `--osui-bottom-bar-item-color`, `--osui-bottom-bar-item-icon-active-color`, `--osui-bottom-bar-item-icon-color`

### breadcrumbs

- Category: 04-navigation
- Classes: `.breadcrumbs`, `.breadcrumbs-item`, `.icon`, `.is-rtl`
- CSS variables (CSS API): `--osui-breadcrumbs-item-color`, `--osui-breadcrumbs-separator-color`

### card

- Category: 02-content
- Classes: `.card`, `.layout-native`
- CSS variables (CSS API): `--osui-card-background`, `--osui-card-border-color`, `--osui-card-border-radius`, `--osui-card-border-width`, `--osui-card-padding`, `--osui-card-shadow`

### card-background

- Category: 02-content
- Classes: `.bottom-center`, `.bottom-left`, `.bottom-right`, `.card-background`, `.card-background-color`, `.center`, `.center-left`, `.center-right`, `.layout-native`, `.padding-none`, `.remove-card-gradient`, `.top-center`, `.top-left`, `.top-right`
- CSS variables (CSS API): `--osui-card-background-border-color`, `--osui-card-background-border-radius`, `--osui-card-background-border-width`

### card-item

- Category: 02-content
- Classes: `.card-detail`
- CSS variables (CSS API): `--osui-card-detail-text-color`, `--osui-card-detail-title-color`

### card-sectioned

- Category: 02-content
- Classes: `.btn`, `.card`, `.card-content`, `.card-image`, `.card-sectioned-top`, `.card-title`, `.flex-direction-column`, `.flex-direction-row`, `.layout-native`, `.padding-none`, `.phone`, `.tablet`
- CSS variables (CSS API): `--card-sectioned-bottom-padding-block`, `--card-sectioned-bottom-padding-inline`, `--card-sectioned-gap`, `--card-sectioned-image-padding-block`, `--card-sectioned-image-padding-inline`, `--card-sectioned-padding`, `--card-sectioned-top-padding-block`, `--card-sectioned-top-padding-inline`

### center-content

- Category: 06-utilities
- Classes: `.animate`, `.center-content`

### chat-message

- Category: 02-content
- Classes: `.chat`, `.hidden`, `.right`
- CSS variables (CSS API): `--osui-chat-message-background`, `--osui-chat-message-border-radius`, `--osui-chat-message-sent-background`, `--osui-chat-message-sent-color`, `--osui-chat-message-status-color`, `--osui-chat-message-status-line-height`

### columns

- Category: 01-adaptive
- Classes: `.card`, `.columns`, `.columns2`, `.columns3`, `.columns4`, `.columns5`, `.columns6`, `.columns-item`, `.columns-medium-left`, `.columns-medium-right`, `.columns-small-left`, `.columns-small-right`, `.get`, `.gutter`, `.gutter-none`, `.phone`, `.phone-break`, `.tablet`, `.tablet-break`

### counter

- Category: 05-numbers
- Classes: `.background-transparent`, `.center-align`, `.counter`, `.flex-direction`

### floating-actions

- Category: 03-interaction
- Classes: `.bottom-bar-exists`, `.desktop`, `.fade-enter`, `.fade-leave`, `.floating`, `.floating-actions-item`, `.floating-actions-item-button`, `.floating-actions-wrapper`, `.floating-button`, `.floating-overlay`, `.has-accessible-features`, `.ios`, `.is--open`, `.landscape`, `.layout-native`, `.no-rotation`, `.phone`, `.portrait`, `.slide-from-bottom-enter`, `.slide-from-bottom-leave`, `.slide-from-left-enter`, `.slide-from-left-leave`, `.slide-from-right-enter`, `.slide-from-right-leave`, `.slide-from-top-enter`, `.slide-from-top-leave`, `.tablet`
- CSS variables (CSS API): `--osui-floating-actions-button-background`, `--osui-floating-actions-button-background-hover`, `--osui-floating-actions-button-background-press`, `--osui-floating-actions-button-border-radius`, `--osui-floating-actions-button-color`, `--osui-floating-actions-button-shadow`, `--osui-floating-actions-item-background`, `--osui-floating-actions-item-background-hover`, `--osui-floating-actions-item-background-press`, `--osui-floating-actions-item-border-radius`, `--osui-floating-actions-item-color`, `--osui-floating-actions-item-shadow`, `--osui-floating-actions-layer`

### floating-content

- Category: 02-content
- Classes: `.absolute`, `.absolute-bottom`, `.absolute-center`, `.absolute-left`, `.absolute-top`, `.absolute-top-plus-header`, `.aside`, `.aside-expandable`, `.aside-visible`, `.blank`, `.desktop`, `.floating`, `.floating-content`, `.floating-content-bottom`, `.floating-content-full`, `.floating-content-full-height`, `.floating-content-full-width`, `.floating-content-margin`, `.landscape`, `.layout`, `.layout-native`, `.layout-side`, `.menu-visible`, `.os-high-contrast`, `.OSInline`, `.phone`, `.tablet`

### icon-badge

- Category: 05-numbers
- Classes: `.badge`, `.bottom-bar-wrapper`, `.icon`, `.icon-badge`, `.layout-native`
- CSS variables (CSS API): `--osui-icon-badge-ring-color`, `--osui-icon-badge-shadow`

### input-with-icon

- Category: 03-interaction
- Classes: `.fa-fw`, `.form`, `.form-control`, `.input-with-icon`, `.input-with-icon-content-icon`, `.input-with-icon-input`, `.input-with-icon-right`, `.not-valid`, `.search-actions`, `.validation-message`
- CSS variables (CSS API): `--osui-input-with-icon-icon-color`, `--osui-input-with-icon-icon-hover-color`

### lightbox-image

- Category: 03-interaction
- Classes: `.android`, `.has-accessible-features`, `.hide-lightbox-image`, `.ios`, `.is-rtl`, `.lightbox`, `.lightbox-image`, `.lightbox-item`, `.phone`, `.pswp`, `.pswp__caption__center`, `.pswp__counter`, `.pswp__top-bar`

### list-item-content

- Category: 02-content
- Classes: `.icon`, `.is-rtl`, `.layout-native`, `.list-item-content`, `.list-item-float`
- CSS variables (CSS API): `--osui-list-item-content-icon-color`, `--osui-list-item-content-icon-size`, `--osui-list-item-content-text-color`, `--osui-list-item-content-text-line-height`, `--osui-list-item-content-title-color`

### list-updating

- Category: 06-utilities
- Classes: `.list-updating`
- CSS variables (CSS API): `--webkit-animation`

### margin-container

- Category: 06-utilities
- Classes: `.layout-native`, `.margin-container`, `.phone`, `.tablet`

### master-detail

- Category: 01-adaptive
- Classes: `.active`, `.android`, `.desktop`, `.has-accessible-features`, `.ios`, `.is-`, `.layout-native`, `.list-item`, `.list-item-content`, `.list-item-selected`, `.open`, `.phone`, `.split`, `.split-left`, `.split-right`, `.split-right-close`, `.split-right-content`, `.split-right--placeholder`, `.split-screen-wrapper`, `.tablet`
- CSS variables (CSS API): `--osui-master-detail-background`, `--osui-master-detail-border-color`, `--osui-master-detail-border-radius`, `--webkit-transform`

### pagination

- Category: 04-navigation
- Classes: `.desktop`, `.form-control`, `.icon`, `.is--active`, `.is--ellipsis`, `.is-rtl`, `.list`, `.list-group`, `.os-high-contrast`, `.pagination`, `.pagination-button`, `.phone`, `.tablet`
- CSS variables (CSS API): `--osui-pagination-active-background`, `--osui-pagination-active-border-color`, `--osui-pagination-active-color`, `--osui-pagination-button-background`, `--osui-pagination-button-border-color`, `--osui-pagination-button-border-radius`, `--osui-pagination-button-border-width`, `--osui-pagination-button-font-weight`, `--osui-pagination-button-hover-background`, `--osui-pagination-button-padding`, `--osui-pagination-button-size`, `--osui-pagination-button-text-color`, `--osui-pagination-counter-color`

### provider-login-button

- Category: 06-utilities
- Classes: `.btn`, `.btn-`, `.btn-provider-login`, `.btn-provider-login-logo-only`, `.btn-provider-login-text`, `.btn-provider-login-text-name`, `.btn-small`, `.layout`, `.phone`, `.rounded`, `.soft`
- CSS variables (CSS API): `--osui-provider-login-button-background`, `--osui-provider-login-button-border-color`, `--osui-provider-login-button-color`

### pull-to-refresh

- Category: 06-utilities
- Classes: `.content`, `.fade-enter`, `.fade-leave`, `.genericon`, `.ios`, `.ios-bounce`, `.layout-native`, `.main`, `.osui-bottom-sheet`, `.osui-bottom-sheet--is-active`, `.ptr`, `.ptr-refresh`, `.pull-to-refresh`, `.pull-to-refresh-loading`, `.slide-from-bottom-enter`, `.slide-from-bottom-leave`, `.slide-from-left-enter`, `.slide-from-left-leave`, `.slide-from-right-enter`, `.slide-from-right-leave`, `.slide-from-top-enter`, `.slide-from-top-leave`

### rangeslider-odc

- Category: 03-interaction

### scrollable-area

- Category: 03-interaction
- Classes: `.carousel`, `.compact`, `.horizontal-scroll`, `.is-rtl`, `.list`, `.list-group`, `.none`, `.osui`, `.ScrollableArea`, `.scrollable-area`, `.vertical-scroll`
- CSS variables (CSS API): `--scrollable-area-height`, `--scrollable-area-width`

### section

- Category: 02-content
- Classes: `.android`, `.ios`, `.is--sticky`, `.layout-native`, `.phone`, `.section`, `.section-group`, `.section-title`, `.sticky`, `.tablet`
- CSS variables (CSS API): `--osui-section-content-color`, `--osui-section-content-line-height`, `--osui-section-group-gap`, `--osui-section-title-border-color`, `--osui-section-title-color`, `--osui-section-title-line-height`

### separator

- Category: 06-utilities
- Classes: `.separator`

### stacked-cards

- Category: 03-interaction
- Classes: `.init`, `.left`, `.list`, `.list-group`, `.OSAutoMarginTop`, `.right`, `.stackedcards`, `.stackedcards-container`, `.stackedcards-overlay`, `.top`
- CSS variables (CSS API): `--osui-stacked-cards-background`, `--osui-stacked-cards-overlay-color`, `--osui-stacked-cards-overlay-left-background`, `--osui-stacked-cards-overlay-right-background`, `--osui-stacked-cards-overlay-top-background`

### tag

- Category: 02-content
- Classes: `.background`, `.background-`, `.background-error-light`, `.background-info-light`, `.background-primary-lightest`, `.background-secondary-lightest`, `.background-success-light`, `.background-warning-light`, `.border-radius-soft`, `.get`, `.tag`
- CSS variables (CSS API): `--osui-tag-color`, `--osui-tag-medium-soft-border-radius`, `--osui-tag-on-light-color`, `--osui-tag-soft-border-radius`

### timeline

- Category: 04-navigation
- Classes: `.timeline`, `.timeline-content`, `.timeline-content-inner`, `.timeline-icon`, `.timeline-icon-line`, `.TimelineItem`
- CSS variables (CSS API): `--osui-timeline-content-gap`, `--osui-timeline-content-spacing`, `--osui-timeline-font-size`, `--osui-timeline-gap`, `--osui-timeline-gutter`, `--osui-timeline-icon-background-color`, `--osui-timeline-icon-color`, `--osui-timeline-icon-size`, `--osui-timeline-line-color`, `--osui-timeline-line-height`, `--osui-timeline-text-color`, `--osui-timeline-title-color`, `--osui-timeline-title-font-weight`

### user-avatar

- Category: 02-content
- Classes: `.avatar`, `.avatar-medium`, `.avatar-small`, `.background`, `.background-`, `.background-error-light`, `.background-info-light`, `.background-primary-lightest`, `.background-secondary-lightest`, `.background-success-light`, `.background-warning-light`, `.border-radius-soft`, `.get`
- CSS variables (CSS API): `--osui-avatar-color`, `--osui-avatar-on-light-color`, `--osui-avatar-soft-border-radius`

---

## Other SCSS groups (foundations, layout, widgets, utilities)

- **00-abstract:** index, mixins, setup-global-vars
- **01-foundations:** html-elements-headings, html-elements-img, html-elements-link, icon-library-o11, icon-library-odc, resets, root
- **02-layout:** content, header, header-layout-native, header-layout-side, ios-bounce, layout, login, menu, menu-app-login-info, menu-app-menu-links, menu-header-logo, menu-layout-native, menu-layout-side, section, themegrid-container
- **03-widgets:** btn, bulk-actions, button-group, checkbox, dropdown, feedback-message, form, inputs-and-textareas, list, list-item, popover, popover-odc, popup, radio-button, switch, table, upload
- **05-useful:** a11y, border-radius, border-size, box-height, box-width, colors-brand, colors-neutral, colors-others, colors-palette, colors-semantic, display, display-align, display-flex, images, miscellaneous, overflow, positioning, positioning-absolute, shadow, space-margin, space-padding, text, typography, visibility
- **06-screen-transitions:** screen-transitions
- **07-keyframes:** animate, btn-loading, feedback-message, list-item, miscellaneous, pull-to-refresh
- **08-servicestudio-preview:** placeholder-empty-o11, placeholder-empty-odc, servicestudiopreview, deprecated-preview
- **09-excluders:** excluders

