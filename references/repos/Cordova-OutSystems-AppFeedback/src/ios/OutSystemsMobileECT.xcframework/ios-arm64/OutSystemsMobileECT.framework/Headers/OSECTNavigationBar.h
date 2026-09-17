//
//  OSECTNavigationBar.h
//  OutSystemsMobileECT
//
//  Created by engineering on 02/12/14.
//  Copyright (c) 2014 OutSystems. All rights reserved.
//

#ifndef OutSystemsMobileECT_OSECTNavigationBar_h
#define OutSystemsMobileECT_OSECTNavigationBar_h

#import <UIKit/UIKit.h>
#import <OutSystemsMobileECT/OSECTUIView.h>

enum kOSECTNavigationBarActions{
    ectNavBarCloseECT,
    ectNavBarHelpECT,
};

@interface OSECTNavigationBar : OSECTUIView

@property (strong, nonatomic) NSNumber *statusBarOffset;
    
-(void)moveBarToTop;
-(void)moveBarToOriginalPosition;

-(BOOL)isNearBy:(CGPoint) point;

-(void)addTarget:(id)target andSelector:(SEL)selector forAction:(enum kOSECTNavigationBarActions)action;

-(void)showButtons:(BOOL)show;

@end

#endif
