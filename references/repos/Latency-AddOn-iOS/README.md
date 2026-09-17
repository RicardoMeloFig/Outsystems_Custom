#LatencyWarning - OutSystems Latency Add-on v1.0

This Add-On **only works with OutSystems Servers** because it calls a specific service on OutSystems to measure the latency.

Helper addon that allows to:

- Measure Latency
- Warning the user if there is a slow connection

LatencyWarning add-on is constituted of two classes:

- [`LatencyWarning`]
- [`LatencyDetection`]

---

## Usage

### Importing files
1. Import source files into project:
  - LatencyWarning.m
  - LatencyWarning.h
  - LatencyDetection.m
  - LatencyDetection.h
2. Make sure the implementation files are added into "Compile Sources" of the desired target
4. That's it.

### Example

```objective-c

// ApplicationViewController.h
#import "LatencyWarning.h"

// rest of the header file...

@end
```

```objective-c
@implementation ApplicationViewController

-(void)viewDidLoad {
  //instantiate LatencyWarning
  LatencyWarning *latency = [[LatencyWarning alloc] initWithHostName:self.infrastructure.hostname andMainView:self.view];
}

@end

```
---
#### Contributors
- OutSystems - Mobility Experts
    - João Gonçalves, <joao.goncalves@outsystems.com>
    - Rúben Gonçalves, <ruben.goncalves@outsystems.com>
    - Vitor Oliveira, <vitor.oliveira@outsystems.com>

#### Document author
- Vitor Oliveira, <vitor.oliveira@outsystems.com>

###Copyright OutSystems, 2015

---

LICENSE
=======


[The MIT License (MIT)](http://www.opensource.org/licenses/mit-license.html)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
