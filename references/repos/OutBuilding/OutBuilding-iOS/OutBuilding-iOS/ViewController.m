//
//  ViewController.m
//  OutBuilding-iOS
//
//  Created by Engineering on 18/01/2017.
//  Copyright © 2017 OutSystems. All rights reserved.
//

#import "ViewController.h"

#import <GoogleMaps/GoogleMaps.h>

@implementation ViewController

// You don't need to modify the default initWithNibName:bundle: method.

- (void)loadView {
    // Create a GMSCameraPosition that tells the map to display the
    // coordinate 38.717069,-9.238674 at zoom level 24.
    GMSCameraPosition *camera = [GMSCameraPosition cameraWithLatitude:38.717069
                                                            longitude:-9.238674
                                                                 zoom:24];
    GMSMapView *mapView = [GMSMapView mapWithFrame:CGRectZero camera:camera];
    mapView.myLocationEnabled = YES;
    self.view = mapView;
    
    // Creates a marker in the center of the map.
    GMSMarker *marker = [[GMSMarker alloc] init];
    marker.position = CLLocationCoordinate2DMake(38.717069, -9.238674);
    marker.title = @"OutSystems";
    marker.snippet = @"Central Park";
    marker.map = mapView;
    
    CLLocationCoordinate2D southWest = CLLocationCoordinate2DMake(38.716734, -9.238760);
    CLLocationCoordinate2D northEast = CLLocationCoordinate2DMake(38.717768, -9.238255);
    GMSCoordinateBounds *overlayBounds = [[GMSCoordinateBounds alloc] initWithCoordinate:southWest
                                                                              coordinate:northEast];
    
    // Image from http://www.lib.utexas.edu/maps/historical/newark_nj_1922.jpg
    UIImage *icon = [UIImage imageNamed:@"floor1"];
    GMSGroundOverlay *overlay =
    [GMSGroundOverlay groundOverlayWithBounds:overlayBounds icon:icon];
    overlay.bearing = 0;
    overlay.map = mapView;
}

@end
