//
//  OSECTHelperView.h
//  OutSystemsMobileECT
//
//  Created by engineering on 12/11/14.
//  Copyright (c) 2014 OutSystems. All rights reserved.
//

#ifndef OutSystemsMobileECT_OSECTHelperView_h
#define OutSystemsMobileECT_OSECTHelperView_h

#import <UIKit/UIKit.h>
#import <OutSystemsMobileECT/OSECTUIView.h>

@interface OSECTHelperView : OSECTUIView

-(void)calculateECTHelperImage:(UIInterfaceOrientation)toInterfaceOrientation;
-(void)addSingleTap:(id)target withSelector:(SEL)selector;

-(void) openHelperView;
-(void)closeHelperView;

- (void)onTapped:(UIGestureRecognizer *)gestureRecognizer;

@end

#endif
