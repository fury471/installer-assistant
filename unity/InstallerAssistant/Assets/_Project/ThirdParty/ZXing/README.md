# ZXing.Net (third-party)

- `zxing.dll`: ZXing.Net 0.16.11 by Michael Jahn, from the NuGet package `ZXing.Net`
  (file `lib/netstandard2.0/zxing.dll`, md5 01a68c10b89db78c32c90f51135b658e).
- Licence: Apache License 2.0, https://licenses.nuget.org/Apache-2.0 · project: https://github.com/micjahn/ZXing.Net/
- Used by `Scripts/Labels/QrLabelScanner.cs` to decode the cable label's QR code. It only compiles in when the
  Scripting Define Symbol `ZXING_NET` is set (Player Settings → Other Settings, on the Android and Windows tabs).
