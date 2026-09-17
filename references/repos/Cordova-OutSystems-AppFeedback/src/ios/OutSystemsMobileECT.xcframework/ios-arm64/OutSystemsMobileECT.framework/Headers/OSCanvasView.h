//
//  OSCanvasView.h
//  OutSystemsMobileECT
//
//  Created by engineering on 05/11/14.
//  Copyright (c) 2014 OutSystems. All rights reserved.
//

#ifndef OutSystemsMobileECT_OSCanvasView_h
#define OutSystemsMobileECT_OSCanvasView_h

#import <UIKit/UIKit.h>

@interface OSCanvasView : UIView


-(void)setBackgroundImage:(UIImage*)bgImage;

-(UIImage*)getCanvasImage;

-(void)clearCanvas;

-(void)addOnDrawingTarget:(id)target beginSelector:(SEL)beginSelector updateSelector:(SEL)updateSelector endSelector:(SEL)endSelector;

-(void)lockCanvas:(BOOL) lock;

@end

#endif
