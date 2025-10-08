"""
Google Drive integration for syncing files to Silo.
Handles authentication, file listing, and downloading from Google Drive.
"""

import io
import logging
import os
from typing import Optional, List, Dict, Any, Generator
from pathlib import Path

from google.auth.transport.requests import Request
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import InstalledAppFlow
from googleapiclient.discovery import build
from googleapiclient.http import MediaIoBaseDownload
from googleapiclient.errors import HttpError

logger = logging.getLogger(__name__)

# Google Drive API scopes
SCOPES = ['https://www.googleapis.com/auth/drive.readonly']


class GoogleDriveSync:
    """Handles Google Drive synchronization."""
    
    def __init__(self, credentials_path: str = 'credentials.json', token_path: str = 'token_drive.json'):
        """Initialize Google Drive sync.
        
        Args:
            credentials_path: Path to OAuth2 credentials file
            token_path: Path to store/load access token
        """
        self.credentials_path = credentials_path
        self.token_path = token_path
        self.service = None
        self._authenticate()
    
    def _authenticate(self):
        """Authenticate with Google Drive API."""
        creds = None
        
        # Load existing token if available
        if os.path.exists(self.token_path):
            creds = Credentials.from_authorized_user_file(self.token_path, SCOPES)
        
        # If no valid credentials, authenticate
        if not creds or not creds.valid:
            if creds and creds.expired and creds.refresh_token:
                logger.info("Refreshing Google Drive credentials")
                creds.refresh(Request())
            else:
                if not os.path.exists(self.credentials_path):
                    raise FileNotFoundError(
                        f"Credentials file not found: {self.credentials_path}\n"
                        "Please download OAuth2 credentials from Google Cloud Console"
                    )
                
                logger.info("Starting OAuth2 flow for Google Drive")
                flow = InstalledAppFlow.from_client_secrets_file(self.credentials_path, SCOPES)
                creds = flow.run_local_server(port=0)
            
            # Save credentials for future use
            with open(self.token_path, 'w') as token:
                token.write(creds.to_json())
            logger.info(f"Saved credentials to {self.token_path}")
        
        self.service = build('drive', 'v3', credentials=creds)
        logger.info("Google Drive API authenticated successfully")
    
    def list_all_files(self, page_size: int = 100) -> Generator[Dict[str, Any], None, None]:
        """List all files from Google Drive with pagination.
        
        Args:
            page_size: Number of files per page
            
        Yields:
            File metadata dictionaries
        """
        page_token = None
        total_files = 0
        
        try:
            while True:
                # Query for all files, excluding folders and shortcuts
                query = "trashed = false and mimeType != 'application/vnd.google-apps.folder'"
                
                results = self.service.files().list(
                    pageSize=page_size,
                    pageToken=page_token,
                    fields="nextPageToken, files(id, name, mimeType, size, modifiedTime, "
                           "md5Checksum, parents, webContentLink, createdTime, owners, "
                           "webViewLink, thumbnailLink, fullFileExtension, originalFilename)",
                    q=query,
                    supportsAllDrives=True,
                    includeItemsFromAllDrives=True
                ).execute()
                
                files = results.get('files', [])
                total_files += len(files)
                
                logger.info(f"Retrieved {len(files)} files (total: {total_files})")
                
                for file in files:
                    # Skip Google Workspace native files that can't be downloaded as-is
                    mime_type = file.get('mimeType', '')
                    if mime_type.startswith('application/vnd.google-apps.'):
                        # These are Google Docs, Sheets, Slides, etc.
                        # We could export them, but skipping for now
                        logger.debug(f"Skipping Google Workspace file: {file.get('name')} ({mime_type})")
                        continue
                    
                    yield file
                
                page_token = results.get('nextPageToken')
                if not page_token:
                    break
            
            logger.info(f"Completed listing Google Drive files. Total: {total_files}")
        
        except HttpError as error:
            logger.error(f"Error listing Google Drive files: {error}")
            raise
    
    def get_file_path(self, file_id: str, file_name: str) -> str:
        """Construct full path for a file by traversing parent folders.
        
        Args:
            file_id: Google Drive file ID
            file_name: File name
            
        Returns:
            Full path string
        """
        try:
            path_parts = [file_name]
            current_file = self.service.files().get(
                fileId=file_id,
                fields='parents',
                supportsAllDrives=True
            ).execute()
            
            # Traverse up to 10 levels to avoid infinite loops
            max_depth = 10
            depth = 0
            
            while 'parents' in current_file and depth < max_depth:
                parent_id = current_file['parents'][0]
                parent = self.service.files().get(
                    fileId=parent_id,
                    fields='name, parents',
                    supportsAllDrives=True
                ).execute()
                
                path_parts.insert(0, parent.get('name', 'Unknown'))
                current_file = parent
                depth += 1
            
            return '/'.join(path_parts)
        
        except HttpError as error:
            logger.warning(f"Could not construct path for {file_name}: {error}")
            return file_name
    
    def download_file(self, file_id: str, file_name: str) -> io.BytesIO:
        """Download a file from Google Drive.
        
        Args:
            file_id: Google Drive file ID
            file_name: File name (for logging)
            
        Returns:
            BytesIO buffer with file content
        """
        try:
            request = self.service.files().get_media(fileId=file_id, supportsAllDrives=True)
            file_buffer = io.BytesIO()
            downloader = MediaIoBaseDownload(file_buffer, request)
            
            done = False
            while not done:
                status, done = downloader.next_chunk()
                if status:
                    progress = int(status.progress() * 100)
                    logger.debug(f"Downloading {file_name}: {progress}%")
            
            file_buffer.seek(0)
            logger.info(f"Downloaded {file_name} ({file_buffer.getbuffer().nbytes} bytes)")
            return file_buffer
        
        except HttpError as error:
            logger.error(f"Error downloading file {file_name}: {error}")
            raise
    
    def download_file_chunked(self, file_id: str, file_name: str, 
                             chunk_size: int = 10 * 1024 * 1024) -> Generator[bytes, None, None]:
        """Download a file in chunks for large files.
        
        Args:
            file_id: Google Drive file ID
            file_name: File name (for logging)
            chunk_size: Chunk size in bytes (default 10MB)
            
        Yields:
            Chunks of file data
        """
        try:
            request = self.service.files().get_media(fileId=file_id, supportsAllDrives=True)
            file_buffer = io.BytesIO()
            downloader = MediaIoBaseDownload(file_buffer, request, chunksize=chunk_size)
            
            done = False
            total_size = 0
            
            while not done:
                status, done = downloader.next_chunk()
                if status:
                    progress = int(status.progress() * 100)
                    logger.debug(f"Downloading {file_name}: {progress}%")
                
                # Yield current buffer content
                file_buffer.seek(0)
                chunk = file_buffer.read()
                if chunk:
                    total_size += len(chunk)
                    yield chunk
                
                # Reset buffer for next chunk
                file_buffer.seek(0)
                file_buffer.truncate()
            
            logger.info(f"Downloaded {file_name} in chunks ({total_size} bytes total)")
        
        except HttpError as error:
            logger.error(f"Error downloading file {file_name}: {error}")
            raise
    
    def get_file_metadata(self, file_id: str) -> Dict[str, Any]:
        """Get detailed metadata for a specific file.
        
        Args:
            file_id: Google Drive file ID
            
        Returns:
            File metadata dictionary
        """
        try:
            file = self.service.files().get(
                fileId=file_id,
                fields="id, name, mimeType, size, modifiedTime, md5Checksum, "
                       "parents, webContentLink, createdTime, owners, description, "
                       "properties, appProperties, capabilities",
                supportsAllDrives=True
            ).execute()
            return file
        except HttpError as error:
            logger.error(f"Error getting file metadata: {error}")
            raise


class GoogleWorkspaceExporter:
    """Export Google Workspace files (Docs, Sheets, Slides) to common formats."""
    
    # Export MIME type mappings
    EXPORT_FORMATS = {
        'application/vnd.google-apps.document': {
            'format': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            'extension': '.docx'
        },
        'application/vnd.google-apps.spreadsheet': {
            'format': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
            'extension': '.xlsx'
        },
        'application/vnd.google-apps.presentation': {
            'format': 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
            'extension': '.pptx'
        },
        'application/vnd.google-apps.drawing': {
            'format': 'application/pdf',
            'extension': '.pdf'
        },
    }
    
    def __init__(self, service):
        """Initialize exporter with Google Drive service.
        
        Args:
            service: Authenticated Google Drive service
        """
        self.service = service
    
    def can_export(self, mime_type: str) -> bool:
        """Check if a Google Workspace file can be exported.
        
        Args:
            mime_type: File MIME type
            
        Returns:
            True if exportable, False otherwise
        """
        return mime_type in self.EXPORT_FORMATS
    
    def export_file(self, file_id: str, mime_type: str, file_name: str) -> io.BytesIO:
        """Export a Google Workspace file to a downloadable format.
        
        Args:
            file_id: Google Drive file ID
            mime_type: Source MIME type
            file_name: File name (for logging)
            
        Returns:
            BytesIO buffer with exported content
        """
        if not self.can_export(mime_type):
            raise ValueError(f"Cannot export MIME type: {mime_type}")
        
        export_format = self.EXPORT_FORMATS[mime_type]['format']
        
        try:
            request = self.service.files().export_media(fileId=file_id, mimeType=export_format)
            file_buffer = io.BytesIO()
            downloader = MediaIoBaseDownload(file_buffer, request)
            
            done = False
            while not done:
                status, done = downloader.next_chunk()
                if status:
                    progress = int(status.progress() * 100)
                    logger.debug(f"Exporting {file_name}: {progress}%")
            
            file_buffer.seek(0)
            logger.info(f"Exported {file_name} to {export_format}")
            return file_buffer
        
        except HttpError as error:
            logger.error(f"Error exporting file {file_name}: {error}")
            raise
