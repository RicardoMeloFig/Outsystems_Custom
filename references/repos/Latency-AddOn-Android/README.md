#LatencyWarning - OutSystems Latency Add-on v1.0

This Add-On **only works with OutSystems Servers** because it calls a specific service on OutSystems to measure the latency.

Helper addon that allows to:

- Measure Latency
- Warning the user if there is a slow connection

LatencyWarning add-on is constituted of three classes:

- [`LatencyWarning`]
- [`LatencyResult`]
- [`LatencyDetection`]

---

## Usage

### Importing Module
1. Import Module Latency into project.
2. Add in the build.gradle the dependency of module ``` compile project(':latencylibrary') ```
4. That's it.

### Example

```xml

// activity_web_application.xml
<com.outsystems.android.widgets.LatencyWarning
	android:id="@+id/view_lat"
	android:layout_width="match_parent"
	android:layout_height="wrap_content"
	android:layout_alignParentTop="true" />
```

```java
public class WebApplicationActivity

public void onCreate(Bundle savedInstanceState) {
  //Start LatencyWarning
  	LatencyWarning latencyWarning = (LatencyWarning) findViewById(R.id.view_lat);
	latencyWarning.startLatencyCheck(HOST_NAME, getActivity());
}

//Stop LatencyCheker
@Override
protected void onDestroy() {
	LatencyWarning latencyWarning = (LatencyWarning) findViewById(R.id.view_lat);
    latencyWarning.stopLatencyCheck();
}

}

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