"""
Google to Silo Sync - Main Application
Orchestrates synchronization from Google services to Silo storage.
"""

import argparse
import logging
import sys
import signal
from pathlib import Path
from typing import Optional
import time

from state_manager import StateManager
from google_drive import GoogleDriveSync, GoogleWorkspaceExporter
from google_photos import GooglePhotosSync
from silo_client import SiloUploadClient, UploadQueue


# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler('google_sync.log')
    ]
)

logger = logging.getLogger(__name__)


class GoogleSiloSync:
    """Main sync orchestrator."""
    
    def __init__(self,
                 silo_url: str,
                 bucket: str,
                 services: list,
                 credentials_path: str = 'credentials.json',
                 state_db: str = 'google_sync_state.db',
                 num_workers: int = 3,
                 max_retries: int = 5):
        """Initialize the sync orchestrator.
        
        Args:
            silo_url: Silo server URL
            bucket: Target bucket name
            services: List of services to sync ('drive', 'photos')
            credentials_path: Path to Google OAuth credentials
            state_db: Path to state database
            num_workers: Number of upload worker threads
            max_retries: Maximum upload retry attempts
        """
        self.silo_url = silo_url
        self.bucket = bucket
        self.services = services
        self.credentials_path = credentials_path
        
        # Initialize components
        self.state_manager = StateManager(state_db)
        self.silo_client = SiloUploadClient(
            server_url=silo_url,
            bucket=bucket,
            max_retries=max_retries
        )
        self.upload_queue = UploadQueue(
            client=self.silo_client,
            num_workers=num_workers
        )
        
        # Service clients (initialized on demand)
        self.drive_client: Optional[GoogleDriveSync] = None
        self.photos_client: Optional[GooglePhotosSync] = None
        
        # Control flags
        self.running = True
        
        # Setup signal handlers
        signal.signal(signal.SIGINT, self._signal_handler)
        signal.signal(signal.SIGTERM, self._signal_handler)
    
    def _signal_handler(self, signum, frame):
        """Handle shutdown signals."""
        logger.info(f"Received signal {signum}. Shutting down gracefully...")
        self.running = False
    
    def test_connection(self) -> bool:
        """Test connection to Silo server.
        
        Returns:
            True if connection successful, False otherwise
        """
        logger.info(f"Testing connection to Silo at {self.silo_url}")
        return self.silo_client.test_connection()
    
    def sync_google_drive(self, export_workspace_files: bool = False):
        """Sync Google Drive files to Silo.
        
        Args:
            export_workspace_files: Whether to export Google Workspace files
        """
        logger.info("Starting Google Drive sync")
        
        # Initialize Drive client if needed
        if not self.drive_client:
            self.drive_client = GoogleDriveSync(
                credentials_path=self.credentials_path,
                token_path='token_drive.json'
            )
        
        workspace_exporter = None
        if export_workspace_files:
            workspace_exporter = GoogleWorkspaceExporter(self.drive_client.service)
        
        # Start sync session
        session_id = self.state_manager.start_sync_session('drive')
        
        stats = {
            'processed': 0,
            'uploaded': 0,
            'failed': 0,
            'skipped': 0,
            'bytes_uploaded': 0
        }
        
        try:
            # List all files
            for file in self.drive_client.list_all_files():
                if not self.running:
                    logger.info("Sync interrupted by user")
                    break
                
                file_id = file.get('id')
                file_name = file.get('name')
                mime_type = file.get('mimeType')
                
                stats['processed'] += 1
                
                # Check if already synced
                if self.state_manager.is_file_synced(file_id, 'drive'):
                    logger.debug(f"Skipping already synced file: {file_name}")
                    stats['skipped'] += 1
                    continue
                
                # Handle Google Workspace files
                if mime_type.startswith('application/vnd.google-apps.'):
                    if export_workspace_files and workspace_exporter and workspace_exporter.can_export(mime_type):
                        try:
                            logger.info(f"Exporting Google Workspace file: {file_name}")
                            file_buffer = workspace_exporter.export_file(file_id, mime_type, file_name)
                            
                            # Update filename with extension
                            export_ext = workspace_exporter.EXPORT_FORMATS[mime_type]['extension']
                            export_name = f"{file_name}{export_ext}"
                            export_mime = workspace_exporter.EXPORT_FORMATS[mime_type]['format']
                            
                            # Upload
                            result = self.silo_client.upload_file(file_buffer, export_name, export_mime)
                            
                            if result.success:
                                stats['uploaded'] += 1
                                self.state_manager.mark_file_synced(file_id, 'drive', file, result.file_id, self.bucket)
                            else:
                                stats['failed'] += 1
                                logger.error(f"Failed to upload {export_name}: {result.error_message}")
                        
                        except Exception as e:
                            stats['failed'] += 1
                            logger.error(f"Error exporting {file_name}: {e}")
                    else:
                        logger.debug(f"Skipping non-exportable Google Workspace file: {file_name}")
                        stats['skipped'] += 1
                    
                    continue
                
                # Download and upload regular files
                try:
                    # Get file path
                    file_path = self.drive_client.get_file_path(file_id, file_name)
                    file['path'] = file_path
                    
                    logger.info(f"Downloading and uploading: {file_path}")
                    
                    # Download file
                    file_buffer = self.drive_client.download_file(file_id, file_name)
                    
                    # Upload to Silo
                    result = self.silo_client.upload_file(
                        file_buffer,
                        file_name,
                        mime_type or 'application/octet-stream',
                        metadata={'source': 'google_drive', 'path': file_path}
                    )
                    
                    if result.success:
                        stats['uploaded'] += 1
                        file_buffer.seek(0, 2)
                        stats['bytes_uploaded'] += file_buffer.tell()
                        self.state_manager.mark_file_synced(file_id, 'drive', file, result.file_id, self.bucket)
                        logger.info(f"Successfully synced: {file_name}")
                    else:
                        stats['failed'] += 1
                        logger.error(f"Failed to upload {file_name}: {result.error_message}")
                        
                        # Add to upload queue for retry
                        self.state_manager.add_to_upload_queue(file_id, 'drive', file, self.bucket)
                
                except Exception as e:
                    stats['failed'] += 1
                    logger.error(f"Error processing {file_name}: {e}")
                    self.state_manager.add_to_upload_queue(file_id, 'drive', file, self.bucket)
                
                # Log progress every 10 files
                if stats['processed'] % 10 == 0:
                    logger.info(f"Progress: {stats['processed']} processed, "
                              f"{stats['uploaded']} uploaded, {stats['failed']} failed, "
                              f"{stats['skipped']} skipped")
            
            # Complete session
            self.state_manager.complete_sync_session(session_id, stats)
            
            logger.info(f"Google Drive sync completed. Stats: {stats}")
        
        except Exception as e:
            logger.error(f"Google Drive sync failed: {e}", exc_info=True)
            self.state_manager.fail_sync_session(session_id, str(e))
            raise
    
    def sync_google_photos(self):
        """Sync Google Photos to Silo."""
        logger.info("Starting Google Photos sync")
        
        # Initialize Photos client if needed
        if not self.photos_client:
            self.photos_client = GooglePhotosSync(
                credentials_path=self.credentials_path,
                token_path='token_photos.json'
            )
        
        # Start sync session
        session_id = self.state_manager.start_sync_session('photos')
        
        stats = {
            'processed': 0,
            'uploaded': 0,
            'failed': 0,
            'skipped': 0,
            'bytes_uploaded': 0
        }
        
        try:
            # List all media items
            for item in self.photos_client.list_all_media_items():
                if not self.running:
                    logger.info("Sync interrupted by user")
                    break
                
                item_id = item.get('id')
                file_name = item.get('filename')
                
                stats['processed'] += 1
                
                # Check if already synced
                if self.state_manager.is_file_synced(item_id, 'photos'):
                    logger.debug(f"Skipping already synced photo: {file_name}")
                    stats['skipped'] += 1
                    continue
                
                # Normalize item
                normalized_item = self.photos_client.normalize_media_item(item)
                
                try:
                    logger.info(f"Downloading and uploading: {file_name}")
                    
                    # Download media
                    file_buffer = self.photos_client.download_media_item(item)
                    
                    # Upload to Silo
                    result = self.silo_client.upload_file(
                        file_buffer,
                        file_name,
                        normalized_item.get('mimeType', 'application/octet-stream'),
                        metadata={'source': 'google_photos', 'path': normalized_item.get('path')}
                    )
                    
                    if result.success:
                        stats['uploaded'] += 1
                        file_buffer.seek(0, 2)
                        stats['bytes_uploaded'] += file_buffer.tell()
                        self.state_manager.mark_file_synced(item_id, 'photos', normalized_item, result.file_id, self.bucket)
                        logger.info(f"Successfully synced: {file_name}")
                    else:
                        stats['failed'] += 1
                        logger.error(f"Failed to upload {file_name}: {result.error_message}")
                        
                        # Add to upload queue for retry
                        self.state_manager.add_to_upload_queue(item_id, 'photos', normalized_item, self.bucket)
                
                except Exception as e:
                    stats['failed'] += 1
                    logger.error(f"Error processing {file_name}: {e}")
                    self.state_manager.add_to_upload_queue(item_id, 'photos', normalized_item, self.bucket)
                
                # Log progress every 10 items
                if stats['processed'] % 10 == 0:
                    logger.info(f"Progress: {stats['processed']} processed, "
                              f"{stats['uploaded']} uploaded, {stats['failed']} failed, "
                              f"{stats['skipped']} skipped")
            
            # Complete session
            self.state_manager.complete_sync_session(session_id, stats)
            
            logger.info(f"Google Photos sync completed. Stats: {stats}")
        
        except Exception as e:
            logger.error(f"Google Photos sync failed: {e}", exc_info=True)
            self.state_manager.fail_sync_session(session_id, str(e))
            raise
    
    def process_upload_queue(self):
        """Process pending uploads from the queue."""
        logger.info("Processing pending uploads from queue")
        
        pending = self.state_manager.get_pending_uploads()
        
        if not pending:
            logger.info("No pending uploads")
            return
        
        logger.info(f"Found {len(pending)} pending uploads")
        
        # Process each pending upload
        for item in pending:
            if not self.running:
                logger.info("Queue processing interrupted")
                break
            
            try:
                # Initialize appropriate client
                service = item['service']
                if service == 'drive':
                    if not self.drive_client:
                        self.drive_client = GoogleDriveSync(self.credentials_path, 'token_drive.json')
                    
                    # Download and upload
                    file_buffer = self.drive_client.download_file(item['google_id'], item['file_name'])
                    result = self.silo_client.upload_file(
                        file_buffer,
                        item['file_name'],
                        item['mime_type']
                    )
                
                elif service == 'photos':
                    if not self.photos_client:
                        self.photos_client = GooglePhotosSync(self.credentials_path, 'token_photos.json')
                    
                    # Get media item and download
                    media_item = self.photos_client.get_media_item_metadata(item['google_id'])
                    file_buffer = self.photos_client.download_media_item(media_item)
                    result = self.silo_client.upload_file(
                        file_buffer,
                        item['file_name'],
                        item['mime_type']
                    )
                
                else:
                    logger.warning(f"Unknown service: {service}")
                    continue
                
                # Update status
                if result.success:
                    self.state_manager.update_upload_status(item['id'], 'completed', silo_file_id=result.file_id)
                    logger.info(f"Successfully uploaded queued item: {item['file_name']}")
                else:
                    self.state_manager.update_upload_status(item['id'], 'failed', error_message=result.error_message)
                    logger.error(f"Failed to upload queued item: {item['file_name']}")
            
            except Exception as e:
                logger.error(f"Error processing queued item {item['file_name']}: {e}")
                self.state_manager.update_upload_status(item['id'], 'failed', error_message=str(e))
    
    def show_stats(self):
        """Display sync statistics."""
        stats = self.state_manager.get_sync_stats()
        
        logger.info("=" * 60)
        logger.info("SYNC STATISTICS")
        logger.info("=" * 60)
        logger.info(f"Synced files: {stats['synced_files']}")
        logger.info(f"Pending uploads: {stats['pending_uploads']}")
        logger.info(f"Failed uploads: {stats['failed_uploads']}")
        logger.info("")
        logger.info("Recent sync sessions:")
        
        for session in stats['recent_sessions']:
            logger.info(f"  {session['service']:8s} | {session['started_at'][:19]} | "
                       f"Status: {session['status']:10s} | "
                       f"Uploaded: {session['files_uploaded']:4d} | "
                       f"Failed: {session['files_failed']:3d}")
        
        logger.info("")
        
        upload_stats = self.silo_client.get_stats()
        logger.info("Upload client stats:")
        logger.info(f"  Attempts: {upload_stats['uploads_attempted']}")
        logger.info(f"  Succeeded: {upload_stats['uploads_succeeded']}")
        logger.info(f"  Failed: {upload_stats['uploads_failed']}")
        logger.info(f"  Success rate: {upload_stats.get('success_rate', 0) * 100:.1f}%")
        logger.info(f"  Bytes uploaded: {upload_stats['bytes_uploaded']:,}")
        logger.info(f"  Rate limits hit: {upload_stats['rate_limits_hit']}")
        logger.info("=" * 60)
    
    def run(self):
        """Run the sync process."""
        logger.info("Google to Silo Sync - Starting")
        logger.info(f"Silo URL: {self.silo_url}")
        logger.info(f"Bucket: {self.bucket}")
        logger.info(f"Services: {', '.join(self.services)}")
        
        # Test connection
        if not self.test_connection():
            logger.error("Failed to connect to Silo. Please check the server URL and ensure it's running.")
            return 1
        
        try:
            # Process each service
            for service in self.services:
                if not self.running:
                    break
                
                if service == 'drive':
                    self.sync_google_drive(export_workspace_files=False)
                elif service == 'photos':
                    self.sync_google_photos()
                else:
                    logger.warning(f"Unknown service: {service}")
            
            # Process any failed uploads from queue
            if self.running:
                self.process_upload_queue()
            
            # Show final stats
            self.show_stats()
            
            logger.info("Sync completed successfully")
            return 0
        
        except Exception as e:
            logger.error(f"Sync failed with error: {e}", exc_info=True)
            return 1


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description='Sync Google services (Drive, Photos) to Silo storage',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Sync Google Drive to local Silo
  python main.py --server http://localhost:5000 --bucket my-files --services drive
  
  # Sync both Drive and Photos
  python main.py --server http://localhost:5000 --bucket backup --services drive photos
  
  # Process pending uploads only
  python main.py --server http://localhost:5000 --bucket files --process-queue
  
  # Show statistics
  python main.py --stats-only
        """
    )
    
    parser.add_argument(
        '--server',
        type=str,
        default='http://localhost:5000',
        help='Silo server URL (default: http://localhost:5000)'
    )
    
    parser.add_argument(
        '--bucket',
        type=str,
        default='files',
        help='Target bucket name (default: files)'
    )
    
    parser.add_argument(
        '--services',
        nargs='+',
        choices=['drive', 'photos'],
        default=['drive'],
        help='Google services to sync (default: drive)'
    )
    
    parser.add_argument(
        '--credentials',
        type=str,
        default='credentials.json',
        help='Path to Google OAuth credentials file (default: credentials.json)'
    )
    
    parser.add_argument(
        '--state-db',
        type=str,
        default='google_sync_state.db',
        help='Path to state database (default: google_sync_state.db)'
    )
    
    parser.add_argument(
        '--workers',
        type=int,
        default=3,
        help='Number of upload worker threads (default: 3)'
    )
    
    parser.add_argument(
        '--max-retries',
        type=int,
        default=5,
        help='Maximum upload retry attempts (default: 5)'
    )
    
    parser.add_argument(
        '--process-queue',
        action='store_true',
        help='Process pending uploads from queue only'
    )
    
    parser.add_argument(
        '--stats-only',
        action='store_true',
        help='Show statistics only'
    )
    
    parser.add_argument(
        '--verbose',
        action='store_true',
        help='Enable verbose logging'
    )
    
    args = parser.parse_args()
    
    # Set logging level
    if args.verbose:
        logging.getLogger().setLevel(logging.DEBUG)
    
    # Create sync orchestrator
    sync = GoogleSiloSync(
        silo_url=args.server,
        bucket=args.bucket,
        services=args.services,
        credentials_path=args.credentials,
        state_db=args.state_db,
        num_workers=args.workers,
        max_retries=args.max_retries
    )
    
    # Handle special modes
    if args.stats_only:
        sync.show_stats()
        return 0
    
    if args.process_queue:
        sync.process_upload_queue()
        sync.show_stats()
        return 0
    
    # Run full sync
    return sync.run()


if __name__ == '__main__':
    sys.exit(main())
