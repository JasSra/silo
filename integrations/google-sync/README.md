# Google to Silo Sync Integration

A lightweight, independent Python application that syncs data from Google services (Drive, Photos) to your Silo storage system. Features include resumable sync with state tracking, rate limiting, retry logic, and offline queue support.

## Features

✅ **Google Drive Sync**
- Syncs all files from Google Drive (including shared drives)
- Optional export of Google Workspace files (Docs, Sheets, Slides) to Office formats
- Maintains full folder structure

✅ **Google Photos Sync**
- Syncs all photos and videos from Google Photos
- Organizes by date (Year/Month structure)
- Preserves original quality

✅ **Robust Upload Handling**
- Automatic retry with exponential backoff
- Rate limiting detection and handling
- Offline queue for failed uploads
- Resumable sync (no duplicate uploads)

✅ **State Management**
- SQLite-based state tracking
- Tracks synced files to avoid re-uploading
- Persistent upload queue for failures
- Session statistics and history

✅ **Scalability**
- Handles large files (TB-scale data)
- Configurable worker threads
- Streaming downloads and uploads
- Memory-efficient chunked processing

## Prerequisites

1. **Python 3.8+**
2. **Silo API Server** - Must be running and accessible
3. **Google Cloud Project** with OAuth2 credentials

## Setup

### 1. Install Dependencies

```powershell
cd c:\dev\code\silo\integrations\google-sync
pip install -r requirements.txt
```

### 2. Get Google OAuth2 Credentials

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select existing one
3. Enable the following APIs:
   - Google Drive API
   - Google Photos Library API
4. Go to **APIs & Services** > **Credentials**
5. Click **Create Credentials** > **OAuth client ID**
6. Choose **Desktop app** as application type
7. Download the credentials JSON file
8. Save it as `credentials.json` in this directory

### 3. Configure Silo Server

Ensure your Silo API server is running. Default is `http://localhost:5000`.

To start Silo (from root directory):

```powershell
cd c:\dev\code\silo
.\dev-start.ps1
```

## Usage

### First Time Setup - Authentication

On first run, the application will open a browser window for OAuth authentication. You'll need to:

1. Sign in to your Google account
2. Grant permissions for Drive and/or Photos access
3. The application will save tokens for future use

### Basic Sync Operations

**Sync Google Drive only:**
```powershell
python main.py --server http://localhost:5000 --bucket my-files --services drive
```

**Sync Google Photos only:**
```powershell
python main.py --server http://localhost:5000 --bucket photos --services photos
```

**Sync both Drive and Photos:**
```powershell
python main.py --server http://localhost:5000 --bucket backup --services drive photos
```

**Use a remote Silo server:**
```powershell
python main.py --server https://silo.example.com --bucket files --services drive
```

### Advanced Options

**Process pending uploads only (retry failed uploads):**
```powershell
python main.py --server http://localhost:5000 --bucket files --process-queue
```

**Show statistics:**
```powershell
python main.py --stats-only
```

**Increase worker threads for faster uploads:**
```powershell
python main.py --server http://localhost:5000 --bucket files --services drive --workers 10
```

**Enable verbose logging:**
```powershell
python main.py --server http://localhost:5000 --bucket files --services drive --verbose
```

**Increase retry attempts:**
```powershell
python main.py --server http://localhost:5000 --bucket files --services drive --max-retries 10
```

## Command-Line Arguments

| Argument | Default | Description |
|----------|---------|-------------|
| `--server` | `http://localhost:5000` | Silo server URL |
| `--bucket` | `files` | Target bucket name |
| `--services` | `drive` | Services to sync: `drive`, `photos`, or both |
| `--credentials` | `credentials.json` | Path to Google OAuth credentials |
| `--state-db` | `google_sync_state.db` | Path to state database |
| `--workers` | `3` | Number of upload worker threads |
| `--max-retries` | `5` | Maximum upload retry attempts |
| `--process-queue` | - | Process pending uploads only |
| `--stats-only` | - | Show statistics only |
| `--verbose` | - | Enable verbose logging |

## How It Works

### Architecture

```
┌─────────────────┐
│  Google Drive   │
│  Google Photos  │
└────────┬────────┘
         │ OAuth2
         │ Download
         ▼
┌─────────────────┐
│  Google Sync    │
│   Application   │
│                 │
│  ┌───────────┐  │
│  │  State    │  │
│  │  Manager  │  │
│  └───────────┘  │
└────────┬────────┘
         │ HTTP POST
         │ /api/files/upload
         ▼
┌─────────────────┐
│   Silo API      │
│                 │
│  ┌───────────┐  │
│  │  MinIO    │  │
│  │  Storage  │  │
│  └───────────┘  │
└─────────────────┘
```

### State Management

The application uses SQLite to track:
- **Synced files**: Files successfully uploaded (prevents duplicates)
- **Upload queue**: Failed uploads for retry
- **Sync sessions**: History and statistics
- **Rate limiting**: Backoff state to respect API limits

### Upload Process

1. **Discovery**: List files from Google service
2. **Check**: Query state DB to skip already-synced files
3. **Download**: Stream file from Google API
4. **Upload**: POST to Silo API with retry logic
5. **Track**: Mark file as synced in state DB

### Error Handling

- **Rate Limiting (429)**: Automatic backoff based on `Retry-After` header
- **Server Errors (5xx)**: Exponential backoff with jitter
- **Connection Errors**: Retry with backoff, queue for offline processing
- **Client Errors (4xx)**: Log error, no retry (likely bad request)

### Resumability

If the sync is interrupted (Ctrl+C, crash, network failure):
- Already synced files are tracked in the database
- Next run will skip previously synced files
- Failed uploads are queued for retry with `--process-queue`

## File Structure

```
google-sync/
├── main.py                    # Main orchestrator and CLI
├── state_manager.py          # SQLite state tracking
├── google_drive.py           # Google Drive integration
├── google_photos.py          # Google Photos integration
├── silo_client.py            # Silo upload client with retries
├── requirements.txt          # Python dependencies
├── README.md                 # This file
├── credentials.json          # Google OAuth credentials (you provide)
├── token_drive.json          # Google Drive OAuth token (auto-generated)
├── token_photos.json         # Google Photos OAuth token (auto-generated)
├── google_sync_state.db      # SQLite state database (auto-generated)
└── google_sync.log           # Application log file (auto-generated)
```

## Scaling for Large Data

### Handling TB-scale Data

**Memory-efficient processing:**
- Files are streamed (not loaded entirely into memory)
- Chunked downloads for large files
- Configurable worker threads for parallel uploads

**Rate limiting:**
- Automatic detection of API rate limits
- Exponential backoff prevents hammering the server
- Configurable retry attempts

**Long-running syncs:**
- State persistence ensures resumability
- Progress logging every 10 files
- Graceful shutdown on Ctrl+C

**Network resilience:**
- Automatic retry on connection errors
- Offline queue for failed uploads
- Connection testing before sync starts

## Monitoring and Troubleshooting

### View Statistics

```powershell
python main.py --stats-only
```

Output example:
```
============================================================
SYNC STATISTICS
============================================================
Synced files: 1,247
Pending uploads: 3
Failed uploads: 12

Recent sync sessions:
  drive    | 2025-10-08 14:23:45 | Status: completed  | Uploaded:  450 | Failed:   2
  photos   | 2025-10-08 12:10:33 | Status: completed  | Uploaded:  797 | Failed:  10
  drive    | 2025-10-07 09:15:22 | Status: failed     | Uploaded:    0 | Failed:   0

Upload client stats:
  Attempts: 1,259
  Succeeded: 1,247
  Failed: 12
  Success rate: 99.0%
  Bytes uploaded: 45,678,901,234
  Rate limits hit: 5
============================================================
```

### Check Logs

```powershell
# View recent logs
Get-Content .\google_sync.log -Tail 50

# Follow live logs
Get-Content .\google_sync.log -Wait
```

### Retry Failed Uploads

```powershell
python main.py --server http://localhost:5000 --bucket files --process-queue
```

### Common Issues

**Issue: "Credentials file not found"**
- Ensure `credentials.json` is in the directory
- Download from Google Cloud Console (see Setup section)

**Issue: Rate limiting errors**
- Reduce `--workers` count
- Increase `--max-retries`
- The app will automatically back off, just let it run

**Issue: Connection refused**
- Ensure Silo server is running
- Check `--server` URL is correct
- Test with: `python main.py --server http://localhost:5000 --stats-only`

**Issue: Large files timing out**
- Increase timeout in `silo_client.py` (default: 300s)
- Check network stability
- Failed uploads will be queued for retry

## Security Notes

- **OAuth Tokens**: `token_*.json` files contain access tokens. Keep them secure.
- **No Auth to Silo**: Currently, the integration posts to Silo without authentication (as specified). Add authentication headers in `silo_client.py` if needed.
- **Credentials**: Never commit `credentials.json` or `token_*.json` to version control.

## Extending

### Add More Google Services

1. Create a new module (e.g., `google_gmail.py`)
2. Implement OAuth and data listing
3. Add to `main.py` orchestrator
4. Update CLI arguments

### Add Authentication to Silo

In `silo_client.py`, modify the upload request:

```python
headers = {
    'Authorization': f'Bearer {your_token}'
}

response = requests.post(
    url,
    files=files,
    data=data,
    headers=headers,
    timeout=self.timeout
)
```

### Custom Metadata

Add metadata to uploads in `main.py`:

```python
metadata = {
    'source': 'google_drive',
    'path': file_path,
    'owner': file.get('owners', [{}])[0].get('emailAddress'),
    'created_time': file.get('createdTime')
}

result = self.silo_client.upload_file(
    file_buffer,
    file_name,
    mime_type,
    metadata=metadata
)
```

## License

Part of the Silo project. See main project LICENSE.

## Support

For issues or questions:
1. Check logs: `google_sync.log`
2. Run with `--verbose` for detailed output
3. Check state DB: `google_sync_state.db` (SQLite browser)
4. Review Silo API logs for upload errors
