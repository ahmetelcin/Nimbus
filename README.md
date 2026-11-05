# Nimbus

Nimbus is a small Windows tray app that opens Office files in Google's editors. Double-click a `.pptx`, `.docx` or `.xlsx` file and Nimbus uploads it to your own Google Drive, converts it, and opens it in Google Slides, Docs or Sheets in your browser.

<!-- Demo: remove this comment's markers after adding docs/demo.gif
![Nimbus demo](docs/demo.gif)
-->

Website: <https://ahmetelcin.github.io/Nimbus/>. Privacy policy: <https://ahmetelcin.github.io/Nimbus/privacy.html>.

## Features

- **Opens Office files in Google's editors:**

  | Opens in | File types |
  |---|---|
  | Google Slides | `ppt`, `pptx`, `odp` |
  | Google Docs | `doc`, `docx`, `odt`, `rtf` |
  | Google Sheets | `xls`, `xlsx`, `ods`, `csv` |

- **No duplicate uploads:** if a file hasn't changed, Nimbus opens the existing Google copy.
- **Saves edits back to your PC:** Google edits to documents opened in the last 24 hours are written back to the local file automatically. You can turn this off.
- **Conflict handling:** if a file changed both on Google and on your PC, nothing is overwritten until you choose to keep the local file, use Google's version, or keep both.
- **Link sharing:** copy a private link or a link anyone can view, and stop sharing later.
- **Windows integration:** tray menu, optional "Open with Nimbus" in the right-click menu, and optional start with Windows.
- **Limited Drive access:** Nimbus uses the `drive.file` permission, so it can't see the rest of your Drive.
- **Signed updates:** Nimbus installs only updates with a valid signature.
- **English and Turkish interface.**

## Install

You need Windows 10 version 2004 or later and a Google account. No other software is required.

1. Download the latest version from [Releases](https://github.com/ahmetelcin/Nimbus/releases).
2. Download `Nimbus.exe` and move it to a folder you can write to, for example `%LOCALAPPDATA%\Programs\Nimbus`.
3. Run `Nimbus.exe` and select **Sign in with Google**.
4. In the tray menu, select **Link file types…**, then set Nimbus as the default app for each file type you want. Windows doesn't let apps make this choice for you.

Nimbus is available in English and Turkish. It uses Turkish if that's your Windows language, and English otherwise. You can change this in **Settings…**.

### "Windows protected your PC"

Microsoft Defender SmartScreen shows this warning because `Nimbus.exe` isn't code-signed. To continue, select **More info**, then **Run anyway**. You only need to do this once for each downloaded version.

### Updates

Nimbus checks for a new version once a day and installs only versions with a valid signature. To choose whether it asks first, updates automatically or doesn't check, go to **Settings…** > **Updates**.

If you have v1.0.0, it can't update itself. Select **Close Nimbus** in the tray menu, then replace your `Nimbus.exe` with the new one. Your sign-in and settings stay, and you can delete `client_secret.json` from that folder.

### Google sign-in

Your browser opens Google's sign-in page, which shows the Nimbus name and asks for permission to access Drive files. That's the only permission Nimbus asks for (`drive.file`). It limits Nimbus to files it created itself, so it can't see the rest of your Drive.

## Usage

- **Open a file:** double-click a linked file type, or right-click a supported file and select **Open with Nimbus**.
- **Work with recent files:** in the tray menu, open **Recent files** and pick a file to **Open**, **Save to computer**, **Copy link**, **Stop sharing** or **Show in folder**.
- **Older formats:** `.ppt`, `.doc` and `.xls` files are saved back as `.pptx`, `.docx` and `.xlsx`, because Google can't export to the older formats.
- **Clean up:** **Clean up old files** moves files that have been unchanged for a set number of days to the trash. The default is 30 days, and you can change it in **Settings…**.
- **Other options:** **Settings…** also has the browser to open files in, what happens when you reopen a file whose Google copy changed, notifications, updates and language.

## Privacy

Nimbus has no server and sends nothing to its developer. Your files go straight to your own Drive, and your sign-in token and file backups stay on your PC in `%APPDATA%\Nimbus`. Nimbus also connects to GitHub to check for updates, unless you turn that off. You can remove Nimbus's access at any time from your [Google account permissions](https://myaccount.google.com/permissions). See the [privacy policy](https://ahmetelcin.github.io/Nimbus/privacy.html) for details.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and your own OAuth client:

1. In the [Google Cloud console](https://console.cloud.google.com/), enable the Google Drive API and create an OAuth client of type **Desktop app**.
2. Save its details as `client_secret.json` in the project folder. The build embeds it in the exe, and Release builds fail without it. Git ignores this file, so don't commit it under another name.

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
   Copy-Item publish\Nimbus.exe $target
   ```

   To use a different OAuth client without rebuilding, put its `client_secret.json` next to `Nimbus.exe`. That file takes priority over the embedded one.

Close Nimbus from the tray menu before you replace the exe. If you move it, its file type and startup entries update the next time it starts.

While you're developing, run `pwsh -File dev.ps1`. It closes every running copy of Nimbus, builds the project and starts the new build. Add `-NoStart` to only build.

Interface text is in `Strings.resx` (English) and `Strings.tr.resx` (Turkish). After you add or remove a string in both files, run `pwsh -File tools\gen-strings.ps1`.

## Uninstall

1. Open **Settings…** from the tray menu. Under **File types**, select **Remove link**, then turn off **Start with Windows** and **Right-click menu**.
2. Select **Sign out** to remove Nimbus's access to your account, then select **Close Nimbus**.
3. Delete `%LOCALAPPDATA%\Programs\Nimbus` and `%APPDATA%\Nimbus`.

Files that Nimbus uploaded stay in the `Nimbus` folder in your Drive until you delete them.

## Publish a release

Every release has only these three files:

- `Nimbus.exe`. The website's download button links to `/releases/latest/download/Nimbus.exe`, so a different name breaks it.
- `Nimbus.exe.sha256`, with the SHA-256 hash of that `Nimbus.exe`.
- `Nimbus.exe.sig`, with the Ed25519 signature of that hash.

Nimbus won't install an update if any of them is missing, or if the hash or signature doesn't match. To create the last two files, sign the exact exe you'll publish:

```powershell
dotnet run --project tools\NimbusSign -- sign publish\Nimbus.exe
```

The private key is in `%USERPROFILE%\.nimbus\update-signing.key`, encrypted with a passphrase (Argon2id and AES-256-GCM). It must stay outside this repository. Create it once with `dotnet run --project tools\NimbusSign -- keygen --source UpdateKey.cs`, which also writes the public key into `UpdateKey.cs`. Both commands ask for the passphrase without showing it, so run them in your own terminal window. Back up the key file and remember the passphrase, because installed copies can't update themselves without them.

## License

[MIT](LICENSE)
