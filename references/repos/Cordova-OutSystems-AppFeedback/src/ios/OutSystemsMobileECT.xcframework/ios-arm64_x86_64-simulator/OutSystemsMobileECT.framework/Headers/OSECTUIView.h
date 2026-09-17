//
//  OSECTUIView.h
//  OutSystemsMobileECT
//
//  Created by engineering on 12/11/14.
//  Copyright (c) 2014 OutSystems. All rights reserved.
//

#ifndef OutSystemsMobileECT_OSECTUIView_h
#define OutSystemsMobileECT_OSECTUIView_h

#import <UIKit/UIKit.h>

extern int const kOSECTHitAreaMargin;
extern float const kOSECTToolbarHeight;
extern float const kOSECTSlideTiming;

@interface OSECTUIView : UIView

-(void)createView;

@end

#endif
