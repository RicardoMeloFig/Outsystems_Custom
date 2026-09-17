// @flow

import React, { Component, type Element } from 'react';

import NodeResolver from './NodeResolver';

export type CaptorProps = {
  children: Element<*>,
  onBottomArrive?: (event: SyntheticEvent<HTMLElement>) => void,
  onBottomLeave?: (event: SyntheticEvent<HTMLElement>) => void,
  onTopArrive?: (event: SyntheticEvent<HTMLElement>) => void,
  onTopLeave?: (event: SyntheticEvent<HTMLElement>) => void,
};

class ScrollCaptor extends Component<CaptorProps> {
  isBottom: boolean = false;
  isTop: boolean = false;
  scrollTarget: HTMLElement;
  touchStart: number;

  // Scroll element previous state variables
  previousScrollTop: number = 0;
  previousScrollHeight: number = 0;
  previousClientHeight: number = 0;

  // Event listener flags
  listeningToScroll: boolean = false;
  listeningToTouchStart: boolean = false;
  listeningToTouchMove: boolean = false;

  componentDidMount() {
    this.previousScrollHeight = this.scrollTarget.scrollHeight;
    this.previousClientHeight = this.scrollTarget.previousClientHeight;
    this.refreshListening(this.scrollTarget);
  }

  componentDidUpdate() {
    if (this.previousScrollHeight !== this.scrollTarget.scrollHeight || this.previousClientHeight !== this.scrollTarget.clientHeight) {
      this.previousScrollHeight = this.scrollTarget.scrollHeight;
      this.previousClientHeight = this.scrollTarget.clientHeight;
      this.refreshListening(this.scrollTarget);
    }
  }

  componentWillUnmount() {
    this.stopListening(this.scrollTarget);
  }
  refreshListening(el: HTMLElement) {
    // bail early if no scroll available or already listening to all events
    if (!el || el.scrollHeight <= el.clientHeight)  {
      this.stopListening(el);
      return;
    }

    if (this.listeningToScroll && this.listeningToTouchStart && this.listeningToTouchMove) {
      // already listenning to everything
      return;
    }

    // all the if statements are to appease Flow 😢
    if (typeof el.addEventListener === 'function' && !this.listeningToScroll) {
      el.addEventListener('scroll', this.onScroll, false);
      this.listeningToScroll = true;
    }
    if (typeof el.addEventListener === 'function' && !this.listeningToTouchStart) {
      el.addEventListener('touchstart', this.onTouchStart, false);
      this.listeningToTouchStart = true;
    }
    if (typeof el.addEventListener === 'function' && !this.listeningToTouchMove) {
      el.addEventListener('touchmove', this.onTouchMove, false);
      this.listeningToTouchMove = true;
    }
  }
  stopListening(el: HTMLElement) {
    // all the if statements are to appease Flow 😢
    if (this.listeningToScroll) {
      el.removeEventListener('scroll', this.onScroll, false);
      this.listeningToScroll = false;
    }
    if (this.listeningToTouchStart) {
      el.removeEventListener('touchstart', this.onTouchStart, false);
      this.listeningToTouchStart = false;
    }
    if (this.listeningToTouchMove) {
      el.removeEventListener('touchmove', this.onTouchMove, false);
      this.listeningToTouchMove = false;
    }
  }

  cancelScroll = (event: SyntheticEvent<HTMLElement>) => {
    event.preventDefault();
    event.stopPropagation();
  };
  handleEventDelta = (event: SyntheticEvent<HTMLElement>, delta: number) => {
    const {
      onBottomArrive,
      onBottomLeave,
      onTopArrive,
      onTopLeave,
    } = this.props;
    const { scrollTop, scrollHeight, clientHeight } = this.scrollTarget;
    const target = this.scrollTarget;
    const isDeltaPositive = delta > 0;
    const availableScroll = scrollHeight - clientHeight - scrollTop;
    let shouldCancelScroll = false;

    // reset bottom/top flags
    if (availableScroll > delta && this.isBottom) {
      if (onBottomLeave) onBottomLeave(event);
      this.isBottom = false;
    }
    if (isDeltaPositive && this.isTop) {
      if (onTopLeave) onTopLeave(event);
      this.isTop = false;
    }

    // bottom limit
    if (isDeltaPositive && delta > availableScroll) {
      if (onBottomArrive && !this.isBottom) {
        onBottomArrive(event);
      }
      target.scrollTop = scrollHeight;
      shouldCancelScroll = true;
      this.isBottom = true;

      // top limit
    } else if (!isDeltaPositive && -delta > scrollTop) {
      if (onTopArrive && !this.isTop) {
        onTopArrive(event);
      }
      target.scrollTop = 0;
      shouldCancelScroll = true;
      this.isTop = true;
    }

    // cancel scroll
    if (shouldCancelScroll) {
      this.cancelScroll(event);
    }
  };

  onScroll = (event: SyntheticEvent<HTMLElement>) => {
    const deltaY = event.currentTarget.scrollTop - this.previousScrollTop;
    this.previousScrollTop = event.currentTarget.scrollTop;
    this.handleEventDelta(event, deltaY);
  };
  onTouchStart = (event: SyntheticTouchEvent<HTMLElement>) => {
    // set touch start so we can calculate touchmove delta
    this.touchStart = event.changedTouches[0].clientY;
  };
  onTouchMove = (event: SyntheticTouchEvent<HTMLElement>) => {
    const deltaY = this.touchStart - event.changedTouches[0].clientY;
    this.handleEventDelta(event, deltaY);
  };

  getScrollTarget = (ref: HTMLElement) => {
    this.scrollTarget = ref;
  };

  render() {
    return (
      <NodeResolver innerRef={this.getScrollTarget}>
        {this.props.children}
      </NodeResolver>
    );
  }
}

type SwitchProps = CaptorProps & {
  isEnabled: boolean,
};

export default class ScrollCaptorSwitch extends Component<SwitchProps> {
  static defaultProps = { isEnabled: true };
  render() {
    const { isEnabled, ...props } = this.props;
    return isEnabled ? <ScrollCaptor {...props} /> : this.props.children;
  }
}
