Pod::Spec.new do |s|
  s.name             = "PureeOS"
  s.version          = "2.0.1.OS5"
  s.summary          = "A log collector for iOS."
  s.homepage         = "https://github.com/OutSystems/puree-ios"
  s.license          = "MIT"
  s.author           = { "Tomohiro Moro" => "tomohiro-moro@cookpad.com" }
  s.source           = { :git => "https://github.com/OutSystems/puree-ios.git", :branch => "outsystems", :tag => s.version.to_s }

  s.platform     = :ios, '7.0'
  s.requires_arc = true

  s.source_files = 'Pod/Classes'

  s.dependency 'YapDatabase', '~> 2.9'
end
