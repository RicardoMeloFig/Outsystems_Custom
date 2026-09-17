//
//  OSAlertMessageView.h
//  OutSystemsMobileECT
//
//  Created by engineering on 04/12/14.
//  Copyright (c) 2014 OutSystems. All rights reserved.
//

#ifndef OutSystemsMobileECT_OSAlertMessageView_h
#define OutSystemsMobileECT_OSAlertMessageView_h

#import <Foundation/Foundation.h>
#import <OutSystemsMobileECT/OSAlertView.h>

@interface OSAlertMessageView : OSAlertView

-(id)initWithParent:(id)parent withTitle:(NSString*)title withMessage:(NSString*)message andSelector:(SEL)selector;

@end

#endif
