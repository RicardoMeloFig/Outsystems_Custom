//
//  OSECTStatusView.h
//  OutSystemsMobileECT
//
//  Created by engineering on 12/11/14.
//  Copyright (c) 2014 OutSystems. All rights reserved.
//

#ifndef OutSystemsMobileECT_OSECTStatusView_h
#define OutSystemsMobileECT_OSECTStatusView_h

#import <UIKit/UIKit.h>
#import <OutSystemsMobileECT/OSECTUIView.h>

enum kOSECTStatusMessage{
    ectStatusSendingFeedback,
    ectStatusSendingFailed,
    ectStatusSendingSucceeded
};

enum kOSECTStatusActions{
    ectCancelRetryAction,
    ectRetrySendingAction
};

@interface OSECTStatusView : OSECTUIView

-(void)showStatusMessage:(enum kOSECTStatusMessage)message;
-(void)closeStatusView;

-(void)addTarget:(id)target andSelector:(SEL)selector forAction:(enum kOSECTStatusActions)action;

@end

#endif
