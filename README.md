# ScreenWave

**A SecondWave IT Systems product.**

Speeds up Samsung Tizen TVs by removing the preloaded apps you don't use – through the TV's own
Developer Mode, over your local network or a cable straight from your PC.

## What it does

1. **Connect** – shows you how to switch on the TV's Developer Mode, finds the TV on your network,
   or sets this PC up for a direct Ethernet cable so the TV never needs the internet.
2. **Remove apps** – lists the TV's apps and lets you remove the ones the TV marks as removable
   (preloaded store and partner apps). Samsung's own system apps are never touched.
3. **Speed-up settings** – a checklist of the settings that slow these TVs most (Autorun Smart Hub,
   Viewing Information Services, voice wake-up, a Smart Hub reset and more).
4. **Stop apps coming back** – a block list for AdGuard Home, Pi-hole or your router.
5. **Finish up** – turn Developer Mode off again and put the PC back to normal.

## Privacy

ScreenWave never connects to the internet. It only talks to private local addresses – your TV, and your own
AdGuard Home or Pi-hole if you set one up. The installer also adds Windows Firewall rules that block ScreenWave and
Samsung's sdb from every internet address.
Nothing about you, your TV or your apps is sent to SecondWave IT Systems, GitHub or anyone else –
there's no update check and no tracking. Its log, list of removed apps and checklist stay on your PC
in `%LocalAppData%\ScreenWave`.

## Download

Get `ScreenWave-x.y.z-Setup.exe` from [Releases](../../releases/latest). Windows 10 or 11, 64-bit.

## Licence

ScreenWave is free to use but is not open source. See [LICENSE](LICENSE). It isn't made by or connected
with Samsung. It includes Samsung's sdb tool, unmodified, under the Apache License 2.0.

---

ScreenWave is owned and developed by **SecondWave IT Systems** (Bedford & Central Bedfordshire).
Contact: liam@secondwaveitsystems.cloud
