# Nimbus

Nimbus is a small Windows tray app that opens Office files in Google's editors. Double-click a `.pptx`, `.docx` or `.xlsx` file and Nimbus uploads it to your own Google Drive, converts it, and opens it in Google Slides, Docs or Sheets in your browser.

If you open the same file again and it hasn't changed, Nimbus opens the existing Google copy instead of uploading a new one.

| Opens in | File types |
|---|---|
| Google Slides | `ppt`, `pptx`, `odp` |
| Google Docs | `doc`, `docx`, `odt`, `rtf` |
| Google Sheets | `xls`, `xlsx`, `ods`, `csv` |

Website: <https://ahmetelcin.github.io/Nimbus/>. Privacy policy: <https://ahmetelcin.github.io/Nimbus/privacy.html>.

## Install

You need Windows 10 version 2004 or later and a Google account. No other software is required.

1. Download the latest version from [Releases](https://github.com/ahmetelcin/Nimbus/releases).
2. Extract `Nimbus.exe` and `client_secret.json` into the same folder, for example `%LOCALAPPDATA%\Programs\Nimbus`.
3. Run `Nimbus.exe` and select **Google ile giriş yap** (sign in with Google).
4. In the tray menu, select **Dosya türlerini bağla…**, then set Nimbus as the default app for each file type you want. Windows doesn't let apps make this choice for you.

The app's interface is currently in Turkish.

### "Windows protected your PC"

Microsoft Defender SmartScreen shows this warning because `Nimbus.exe` isn't code-signed. To continue, select **More info**, then **Run anyway**. You only need to do this once for each downloaded version.

### "Google hasn't verified this app"

Google may show this warning when you sign in if it hasn't verified the app. To continue, select **Advanced**, then **Go to Nimbus (unsafe)**. Nimbus only asks for the `drive.file` permission, which limits it to files it created itself. It can't see the rest of your Drive.

If the Google Cloud project is in testing mode, only listed test users can sign in, and they must sign in again every week.

## Privacy

Nimbus has no server and sends nothing to its developer. Your files go straight to your own Drive, and your sign-in token stays on your PC in `%APPDATA%\Nimbus`. You can remove Nimbus's access at any time from your [Google account permissions](https://myaccount.google.com/permissions). See the [privacy policy](https://ahmetelcin.github.io/Nimbus/privacy.html) for details.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and your own OAuth client:

1. In the [Google Cloud console](https://console.cloud.google.com/), enable the Google Drive API and create an OAuth client of type **Desktop app**.
2. Save its details as `client_secret.json` in the project folder. Git ignores this file, so don't commit it under another name.

   ```json
   {
     "installed": {
       "client_id": "YOUR_CLIENT_ID",
       "client_secret": "YOUR_CLIENT_SECRET",
       "auth_uri": "https://accounts.google.com/o/oauth2/auth",
       "token_uri": "https://oauth2.googleapis.com/token",
       "redirect_uris": ["http://localhost"]
     }
   }
   ```

3. Publish a single-file build and copy it to its install folder:

   ```powershell
   dotnet publish -c Release -o publish
   $target = "$env:LOCALAPPDATA\Programs\Nimbus"
   New-Item -ItemType Directory -Force $target | Out-Null
   Copy-Item publish\Nimbus.exe, publish\client_secret.json $target
   ```

Close Nimbus from the tray menu before you replace the exe. If you move it, its file type and startup entries update the next time it starts.

## Uninstall

1. Open **Ayarlar…** from the tray menu. Under **Dosya türleri**, select **Bağlantıyı kaldır**, then turn off **Windows ile başlat**.
2. Select **Hesaptan çıkış yap** to remove Nimbus's access to your account, then select **Nimbus'u kapat**.
3. Delete `%LOCALAPPDATA%\Programs\Nimbus` and `%APPDATA%\Nimbus`.

Files that Nimbus uploaded stay in the `Nimbus` folder in your Drive until you delete them.

## License

[MIT](LICENSE)
