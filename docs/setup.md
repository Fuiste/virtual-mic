# setup and first test

virtual mic mixes your physical microphone and sound pads into one input for a call app. this prototype uses a separately installed **vb-cable** driver. the microphone listed in discord/slack will be **cable output**, not an endpoint named "virtual mic".

## what you need

- windows 11 x64 for this build.
- the portable virtual mic folder, including `VirtualMic.exe` and its accompanying notices. no separate .net installation is needed for that executable.
- a physical microphone and headphones. wired headphones are a useful first-test baseline.
- vb-cable installed once with administrator privileges, then a windows restart.

## 1. install vb-cable

1. save your work and finish any active calls before driver installation.
2. go to the [official vb-cable download page](https://vb-audio.com/Cable/) and download the windows package. avoid third-party driver download sites.
3. extract the **entire** zip to a folder. do not run the setup executable from inside the zip: it needs the accompanying driver files.
4. on this x64 build, right-click `VBCABLE_Setup_x64.exe` and choose **run as administrator**. approve the windows elevation prompt for the verified vb-audio publisher.
5. click **install driver** and wait for the result. the publisher says the installer can briefly appear unresponsive; wait for its completion message.
6. restart windows when convenient. the publisher explicitly requires a reboot to finalize installation, even if the devices already appear. [official installation instructions](https://vb-audio.com/Cable/)
7. after installation/reboot, check **windows settings > system > sound > output**. if windows selected cable input as its default output, select your physical headphones/speakers again. this happened on our local test machine; leaving the cable as the system output can silence your speakers or send unrelated desktop audio into your call.

you only need one standard cable for this app. vb-cable is donationware, separately licensed and distributed by its publisher. our app does not bundle its installer or change your windows default audio devices, but windows/driver installation can select a new default as noted above. installing the driver does not configure discord or slack for you.

after restarting, windows sound settings should list both:

| windows device type | device | purpose |
| --- | --- | --- |
| output / playback | cable input (vb-audio virtual cable) | virtual mic sends the finished mix here |
| input / recording | cable output (vb-audio virtual cable) | discord/slack receives the finished mix here |

the words input/output are from the **cable's** perspective. [vb-audio explains the playback-to-recording connection here](https://vb-audio.com/Cable/).

## 2. set up virtual mic

open `VirtualMic.exe`. in a source checkout, the portable build is under `artifacts/virtual-mic-win-x64/`; in an extracted portable package, the executable is at the top level. the app starts with audio stopped.

| control | select / set |
| --- | --- |
| microphone | your physical mic or the interface input carrying it |
| virtual output | cable input (vb-audio virtual cable) |
| monitor | the physical headphones/output you normally listen through |
| listen | off for the first remote-input check; on to hear sound pads locally |
| hear microphone | off initially; enable later with headphones to audition voice effects |
| bass boost / distortion | off for the first baseline test |

click **refresh** if the cable was installed after opening the app. when multiple cable choices appear, use the standard **cable input** for the first stereo test rather than an optional multichannel endpoint. stop the engine before changing a device selector.

start with microphone **100%**, sounds **70%**, master **80%**, and monitor **50%**; these are the app defaults, not calibrated levels for every microphone. adjust your interface's physical input gain if needed. reduce levels if **limiting · lower gain** appears repeatedly.

add files with **+ add sounds**, drag files onto the window, or use **add sample sounds** on an empty board. the starter tones are original synthesized audio; the screenshot's meme-style pad names are examples, not included recordings.

click **start virtual mic**. the status badge should change to **live**, and speaking should move the mic and output meters. an input pair such as an interface's "input 1/2" is a stereo pair; this prototype preserves those channels and does not yet have individual interface-channel selection.

## 3. select the mix in your call app

### discord

1. open **user settings > voice & video** and find the voice controls.
2. set **input device** to **cable output (vb-audio virtual cable)**.
3. set **output device** to your physical headphones, not cable input.
4. use discord's mic test to check speech, then play a sound pad. exit the mic test afterward so its delayed playback is not confused with virtual mic's own monitor.
5. for the baseline test, use voice activity or deliberately hold your push-to-talk key while testing. a muted/closed input gate also blocks soundboard clips.

if sounds are audible in the app's monitor but disappear or get chopped up in discord, try turning off noise suppression/krisp for the test and compare again. check input sensitivity and any other voice processing available in your client. speech-oriented processing may treat sound effects as unwanted noise; this is a troubleshooting hypothesis to test, not a guarantee for every clip. [discord's device and mic-test instructions](https://support.discord.com/hc/en-us/articles/360045138471-Discord-Voice-and-Video-Troubleshooting-Guide)

### slack

1. open your profile menu, then **preferences > audio & video**.
2. under microphone, select **cable output (vb-audio virtual cable)**.
3. under speaker, select your physical headphones.
4. check the input meter while speaking and while playing a pad, then test in a huddle when ready.
5. if clips are suppressed or their level pumps, compare with noise suppression and automatic gain control disabled, where available. change one setting at a time.

slack documents microphone/speaker selection and microphone processing in its [huddle preferences guide](https://slack.com/intl/en-gb/help/articles/1500002037922-Adjust-your-huddles-preferences). menu wording can vary by client version.

for another app, use the same rule: **input = cable output; speakers = physical headphones**. there is no need to make the cable your windows-wide default device.

## 4. five-minute first-test checklist

use a private mic test first. only join a call when you are ready to transmit audio.

- [ ] **voice:** start virtual mic, speak, and confirm the call app's input meter responds.
- [ ] **sound:** play a starter pad and confirm the call app receives it.
- [ ] **mix:** speak while a sound plays; both should be present.
- [ ] **monitor:** enable listen. you should hear the sound locally; your remote mix should continue unchanged.
- [ ] **voice monitor:** enable hear microphone with headphones. disable it again if the monitoring delay is distracting.
- [ ] **bass:** leave the row target on **mic**, enable bass boost, move the control, and speak. the voice should change; sound pads should remain clean.
- [ ] **distortion:** enable distortion, raise drive/wet mix gradually, and speak. reduce levels if peak guard is repeatedly active.
- [ ] **stop sounds:** play overlapping pads, then press stop sounds or escape. clips should stop and speech should continue.
- [ ] **mic mute:** mute the microphone inside virtual mic. speech should stop; pads should still transmit.
- [ ] **stop engine:** stop virtual mic. both speech and pads should stop reaching the cable.
- [ ] **restart:** close/reopen the app. pads/effects should persist, while the engine and monitoring start off.

number keys 1-9 and escape work only while the app window is focused. there are no global hotkeys or tray/background controls yet.

## effects chain / 0.2 preview

the **effects** panel runs top to bottom. choose an effect and click **+** to add a row; use its arrows to reorder, checkbox to bypass, or **×** to remove. each row targets **mic**, **sounds**, or **both**. both processes the sources separately so sounds-only monitoring never includes your microphone's effects. default bass/distortion rows target mic, preserving the original behavior.

to test routing, play a pad while speaking and switch distortion between the three targets. enable **hear microphone** to hear changes to your voice. **stop sounds** also clears sound-effect tails; normal pad endings let tails finish. chain edits are immediate and may click. plugins and their parameters are saved with your library.

click **plugins** to see loaded effects/errors and open the installation folder. restart after adding/removing plugins. friends can build c# dlls using the [plugin guide and delay reference example](plugins.md); it also explains trusted-code requirements and recovery if a plugin prevents startup. plugin support requires the 0.2 source/build, rather than the original 0.1 public zip.

## what each stop/mute control does

| control | voice to call | pads to call | local monitoring |
| --- | --- | --- | --- |
| stop sounds / escape | continues | stops current pads | stopped pads also stop locally |
| microphone mute in virtual mic | muted | continues | monitored voice is muted |
| listen off | unchanged | unchanged | off |
| mute in discord/slack | blocked by that call app | also blocked by that call app | unchanged in virtual mic |
| stop virtual mic / close app | stops | stops | stops |

## troubleshooting

| symptom | check |
| --- | --- |
| no cable in the virtual-output list | finish the driver install, restart windows, reopen the app, and refresh devices. check that cable input is enabled in windows playback devices. |
| no speech meter | confirm the physical input and interface gain; unmute the mic. check windows microphone access for desktop apps in settings > privacy & security > microphone. |
| start briefly lights up, then stops | read the error beside the start button. detailed exception information is saved in `%LOCALAPPDATA%\VirtualMic\logs\audio.log`. the early preview's monitor buffer error is fixed in the current build. |
| no local monitoring | enable **listen** for pads; also enable **hear microphone** for your voice. these switches reset off when the app opens. select your physical headphone output. |
| headphone monitoring stops | the mic/cable route keeps running. retry **listen**; if the device is missing, stop the engine, refresh devices, select an available output, and start again. |
| app meters move, but the call gets silence | select cable output as the call microphone. check call mute, push-to-talk, and input sensitivity. cable input is the wrong choice for the call microphone. |
| pads are audible locally but not in the call | local monitoring alone does not prove the call route. check the selected call input, gate, and noise suppression. |
| repeated/echoing audio | use headphones. disable windows "listen to this device" on the cable; avoid running both the call app's mic-test playback and this app's voice monitor. |
| people hear their own call audio back | route the call's speaker output to headphones, never cable input. check that windows has not switched its default playback device to the cable during installation. |
| distortion with effects off | reduce mic/sound/master levels and the physical preamp gain; watch for peak guard activity. the peak guard prevents excessive digital amplitude but is not a transparent mastering limiter. |
| voice in one ear | some audio interfaces expose a stereo pair with the mic on one channel. check interface routing; individual channel selection/mono folding is not implemented yet. |
| "audio stopped" after unplug/sleep | stop, reconnect the device, refresh, select the route, then explicitly start again. missing devices are not silently replaced. |
| sound import fails | use wav, mp3, or aiff; keep each clip at two minutes or less. there are 24 pads and a shared 256 mib decoded-audio budget. |
| renamed/moved source file | existing imported pads should still work: the app stores independent copies. re-add the sound if its internal copy is missing. |
| startup reports an unreadable library | close the app and copy the entire library folder somewhere safe. preserve the broken file; the previous `library.json.bak` can be copied back as `library.json` for recovery. |

## backup and undo

the library lives at `%LOCALAPPDATA%\VirtualMic`. copy that **whole folder**, including `sounds/`, while the app is closed. imported originals are untouched. removing a pad retains its internal audio copy for recovery.

to stop using the app, close it and select your physical microphone directly in discord/slack. the portable app does not require an uninstaller.

to remove vb-cable, first move any apps using it back to their physical devices. run the official setup again as administrator, choose **remove driver**, then restart windows. this removes the shared cable used by any app, not just virtual mic. [publisher reboot guidance](https://vb-audio.com/Cable/)

## useful test feedback

when reporting a problem, note the mic/interface, headphone output, call client, which meters moved, whether local monitoring worked, and whether effects/noise suppression were enabled. mention the exact action that failed and any on-screen error. do not include device ids or personal audio unless needed and intentionally shared.

audio error logs contain timestamps, exception messages, and stack traces; they do not record audio. they stay local and rotate at approximately 256 kib with one previous file. review logs before sharing them.
