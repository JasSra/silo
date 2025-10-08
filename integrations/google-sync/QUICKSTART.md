# Quick Start Guide - Google to Silo Sync

This guide will get you up and running in 5 minutes.

## Prerequisites Check

- [ ] Python 3.8+ installed (`python --version`)
- [ ] Silo API server accessible
- [ ] Google Cloud project with OAuth credentials

## Step 1: Install Dependencies

```powershell
cd c:\dev\code\silo\integrations\google-sync
pip install -r requirements.txt
```

## Step 2: Get Google OAuth Credentials

### Option A: Use Existing Credentials
If you already have `credentials.json`:
1. Copy it to this directory
2. Skip to Step 3

### Option B: Create New Credentials
1. Go to https://console.cloud.google.com/
2. Create/select a project
3. Enable APIs:
   - Google Drive API
   - Google Photos Library API (if syncing photos)
4. Go to **Credentials** → **Create Credentials** → **OAuth client ID**
5. Select **Desktop app**
6. Download JSON and save as `credentials.json` in this directory

## Step 3: Verify Installation

Run the component tests:
```powershell
python test_components.py
```

You should see:
```
ALL COMPONENT TESTS PASSED ✓
```

## Step 4: Start Your First Sync

### For Google Drive:

```powershell
python main.py --server http://localhost:5000 --bucket my-files --services drive
```

### For Google Photos:

```powershell
python main.py --server http://localhost:5000 --bucket photos --services photos
```

### For Both:

```powershell
python main.py --server http://localhost:5000 --bucket backup --services drive photos
```

## What Happens on First Run

1. **Browser Opens**: You'll be redirected to Google to authorize access
2. **Grant Permissions**: Allow the application to access Drive/Photos
3. **Sync Starts**: The application will begin listing and uploading files
4. **Progress Logged**: Check console or `google_sync.log` for progress

## During Sync

The application will:
- Show progress every 10 files
- Automatically retry failed uploads
- Handle rate limiting gracefully
- Save state to resume if interrupted

**To stop**: Press `Ctrl+C` (graceful shutdown)

## After Sync

### View Statistics

```powershell
python main.py --stats-only
```

### Retry Failed Uploads

```powershell
python main.py --server http://localhost:5000 --bucket files --process-queue
```

### Check Logs

```powershell
Get-Content .\google_sync.log -Tail 50
```

## Common First-Run Issues

### "ModuleNotFoundError: No module named 'google'"
**Fix**: Install dependencies with `pip install -r requirements.txt`

### "credentials.json not found"
**Fix**: Download OAuth credentials from Google Cloud Console (see Step 2)

### "Connection refused" or "Connection test failed"
**Fix**: Ensure Silo server is running:
```powershell
cd c:\dev\code\silo
.\dev-start.ps1
```

### "Rate limited"
**Fix**: This is normal for large syncs. The app will automatically back off and retry.

## Configuration Tips

### For Large Libraries (1000+ files)
```powershell
python main.py --server http://localhost:5000 --bucket files --services drive --workers 5 --max-retries 10
```

### For Slow Networks
```powershell
python main.py --server http://localhost:5000 --bucket files --services drive --workers 1
```

### For Remote Silo Server
```powershell
python main.py --server https://silo.example.com --bucket files --services drive
```

## Files Created

After first run, you'll see:
- `token_drive.json` - Google Drive OAuth token (keep secure!)
- `token_photos.json` - Google Photos OAuth token (keep secure!)
- `google_sync_state.db` - SQLite database with sync state
- `google_sync.log` - Application logs

**Never commit these files to version control!**

## Next Steps

- Read the full [README.md](README.md) for advanced usage
- Set up scheduled syncs (Task Scheduler on Windows, cron on Linux)
- Monitor sync statistics regularly
- Review logs for any errors

## Getting Help

If you encounter issues:
1. Check `google_sync.log` for detailed errors
2. Run with `--verbose` flag for more output
3. Verify Silo API is accessible
4. Check Google Cloud Console for API quotas

## Example: Complete First Sync

```powershell
# 1. Install dependencies
pip install -r requirements.txt

# 2. Verify installation
python test_components.py

# 3. Start Silo (if not running)
cd c:\dev\code\silo
.\dev-start.ps1

# 4. Return to google-sync directory
cd integrations\google-sync

# 5. Run sync (will open browser for OAuth)
python main.py --server http://localhost:5000 --bucket my-files --services drive

# 6. Check stats after completion
python main.py --stats-only
```

That's it! Your Google data is now syncing to Silo. 🎉
