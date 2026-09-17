require 'json'
package = JSON.parse(File.read(File.join(__dir__, 'package.json')))

Pod::Spec.new do |spec|
  spec.name                   = package['name']
  spec.version                = package['version']

  spec.summary                = package['description']
  spec.description            = <<-DESC
  The InAppBrowserLib library behaves as a standard web browser and is useful to load untrusted content without risking your application's security.
  
  The `OSIABEngine` structure provides the main features of the Library, which are 3 different ways to open a URL:
  - using an External Browser;
  - using a System Browser;
  - using a Web View.

   the library provides the following features:
  - Open a URL in an External Browser.
  - Open a URL in a System Browser.
  - Open a URL in a Web View.
  DESC

  spec.homepage               = 'https://github.com/OutSystems/OSInAppBrowserLib-iOS'
  spec.license                = { :type => package['license'], :file => 'LICENSE' }
  spec.author                 = { package['author'] => package['email'] }
  
  spec.source                 = { :http => "https://github.com/OutSystems/OSInAppBrowserLib-iOS/releases/download/#{spec.version}/OSInAppBrowserLib.zip", :type => "zip" }
  spec.vendored_frameworks    = "OSInAppBrowserLib.xcframework"

  spec.ios.deployment_target  = '13.0'
  spec.swift_versions         = ['5.0', '5.1', '5.2', '5.3', '5.4', '5.5', '5.6', '5.7', '5.8', '5.9']
end
