//
//  OSECTToolbarView.h
//  OutSystemsMobileECT
//
//  Created by engineering on 12/11/14.
//  Copyright (c) 2014 OutSystems. All rights reserved.
//

#ifndef OutSystemsMobileECT_OSECTToolbarView_h
#define OutSystemsMobileECT_OSECTToolbarView_h

#import <UIKit/UIKit.h>
#import <OutSystemsMobileECT/OSECTUIView.h>

enum kOSECTToolbarActions{
    ectCloseToolbar,
    ectSendFeedback,
    ectRecordAudio,
    ectPlayAudio
};

@interface OSECTToolbarView : OSECTUIView<UITextViewDelegate>

-(void)resetToolbar;
-(void)addTarget:(id)target andSelector:(SEL)selector forAction:(enum kOSECTToolbarActions)action;

-(void)resizeToolbar;

-(NSString*)getTextMessage;

-(void)showTextAreaOrPlayButton:(BOOL)showText;
-(void) showPlayButton:(BOOL)showButton;

@end

#endif
