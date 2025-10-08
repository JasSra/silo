"""
Google Photos integration for syncing media to Silo.
Handles authentication, media listing, and downloading from Google Photos.
"""

import io
import logging
import os
import requests
from typing import Optional, List, Dict, Any, Generator
from datetime import datetime

from google.auth.transport.requests import Request
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import InstalledAppFlow

logger = logging.getLogger(__name__)

# Google Photos API scopes
SCOPES = ['https://www.googleapis.com/auth/photoslibrary.readonly']

# Google Photos API base URL
PHOTOS_API_BASE = 'https://photoslibrary.googleapis.com/v1'


class GooglePhotosSync:
    """Handles Google Photos synchronization."""
    
    def __init__(self, credentials_path: str = 'credentials.json', token_path: str = 'token_photos.json'):
        """Initialize Google Photos sync.
        
        Args:
            credentials_path: Path to OAuth2 credentials file
            token_path: Path to store/load access token
        """
        self.credentials_path = credentials_path
        self.token_path = token_path
        self.credentials = None
        self._authenticate()
    
    def _authenticate(self):
        """Authenticate with Google Photos API."""
        creds = None
        
        # Load existing token if available
        if os.path.exists(self.token_path):
            creds = Credentials.from_authorized_user_file(self.token_path, SCOPES)
        
        # If no valid credentials, authenticate
        if not creds or not creds.valid:
            if creds and creds.expired and creds.refresh_token:
                logger.info("Refreshing Google Photos credentials")
                creds.refresh(Request())
            else:
                if not os.path.exists(self.credentials_path):
                    raise FileNotFoundError(
                        f"Credentials file not found: {self.credentials_path}\n"
                        "Please download OAuth2 credentials from Google Cloud Console"
                    )
                
                logger.info("Starting OAuth2 flow for Google Photos")
                flow = InstalledAppFlow.from_client_secrets_file(self.credentials_path, SCOPES)
                creds = flow.run_local_server(port=0)
            
            # Save credentials for future use
            with open(self.token_path, 'w') as token:
                token.write(creds.to_json())
            logger.info(f"Saved credentials to {self.token_path}")
        
        self.credentials = creds
        logger.info("Google Photos API authenticated successfully")
    
    def _make_request(self, method: str, endpoint: str, **kwargs) -> Dict[str, Any]:
        """Make an authenticated request to Google Photos API.
        
        Args:
            method: HTTP method (GET, POST)
            endpoint: API endpoint
            **kwargs: Additional arguments for requests
            
        Returns:
            Response JSON
        """
        url = f"{PHOTOS_API_BASE}/{endpoint}"
        headers = kwargs.pop('headers', {})
        headers['Authorization'] = f'Bearer {self.credentials.token}'
        
        response = requests.request(method, url, headers=headers, **kwargs)
        response.raise_for_status()
        return response.json()
    
    def list_all_media_items(self, page_size: int = 100) -> Generator[Dict[str, Any], None, None]:
        """List all media items from Google Photos with pagination.
        
        Args:
            page_size: Number of items per page (max 100)
            
        Yields:
            Media item dictionaries
        """
        page_token = None
        total_items = 0
        
        try:
            while True:
                params = {'pageSize': min(page_size, 100)}
                if page_token:
                    params['pageToken'] = page_token
                
                response = self._make_request('GET', 'mediaItems', params=params)
                
                media_items = response.get('mediaItems', [])
                total_items += len(media_items)
                
                logger.info(f"Retrieved {len(media_items)} media items (total: {total_items})")
                
                for item in media_items:
                    yield item
                
                page_token = response.get('nextPageToken')
                if not page_token:
                    break
            
            logger.info(f"Completed listing Google Photos. Total: {total_items}")
        
        except requests.RequestException as error:
            logger.error(f"Error listing Google Photos media items: {error}")
            raise
    
    def list_albums(self) -> List[Dict[str, Any]]:
        """List all albums from Google Photos.
        
        Returns:
            List of album dictionaries
        """
        albums = []
        page_token = None
        
        try:
            while True:
                params = {'pageSize': 50}
                if page_token:
                    params['pageToken'] = page_token
                
                response = self._make_request('GET', 'albums', params=params)
                
                page_albums = response.get('albums', [])
                albums.extend(page_albums)
                
                page_token = response.get('nextPageToken')
                if not page_token:
                    break
            
            logger.info(f"Retrieved {len(albums)} albums")
            return albums
        
        except requests.RequestException as error:
            logger.error(f"Error listing albums: {error}")
            raise
    
    def list_album_media_items(self, album_id: str, page_size: int = 100) -> Generator[Dict[str, Any], None, None]:
        """List all media items in a specific album.
        
        Args:
            album_id: Album ID
            page_size: Number of items per page
            
        Yields:
            Media item dictionaries
        """
        page_token = None
        total_items = 0
        
        try:
            while True:
                body = {
                    'albumId': album_id,
                    'pageSize': min(page_size, 100)
                }
                if page_token:
                    body['pageToken'] = page_token
                
                response = self._make_request('POST', 'mediaItems:search', json=body)
                
                media_items = response.get('mediaItems', [])
                total_items += len(media_items)
                
                logger.info(f"Retrieved {len(media_items)} items from album (total: {total_items})")
                
                for item in media_items:
                    yield item
                
                page_token = response.get('nextPageToken')
                if not page_token:
                    break
            
            logger.info(f"Completed listing album media. Total: {total_items}")
        
        except requests.RequestException as error:
            logger.error(f"Error listing album media items: {error}")
            raise
    
    def download_media_item(self, item: Dict[str, Any]) -> io.BytesIO:
        """Download a media item from Google Photos.
        
        Args:
            item: Media item dictionary
            
        Returns:
            BytesIO buffer with media content
        """
        base_url = item.get('baseUrl')
        if not base_url:
            raise ValueError(f"No baseUrl found for item: {item.get('id')}")
        
        # For photos, append =d to download full resolution
        # For videos, append =dv to download video
        mime_type = item.get('mimeType', '')
        if mime_type.startswith('video/'):
            download_url = f"{base_url}=dv"
        else:
            download_url = f"{base_url}=d"
        
        try:
            response = requests.get(download_url, stream=True)
            response.raise_for_status()
            
            file_buffer = io.BytesIO()
            total_size = 0
            
            for chunk in response.iter_content(chunk_size=8192):
                if chunk:
                    file_buffer.write(chunk)
                    total_size += len(chunk)
            
            file_buffer.seek(0)
            logger.info(f"Downloaded {item.get('filename')} ({total_size} bytes)")
            return file_buffer
        
        except requests.RequestException as error:
            logger.error(f"Error downloading media item {item.get('filename')}: {error}")
            raise
    
    def download_media_item_chunked(self, item: Dict[str, Any], 
                                   chunk_size: int = 10 * 1024 * 1024) -> Generator[bytes, None, None]:
        """Download a media item in chunks for large files.
        
        Args:
            item: Media item dictionary
            chunk_size: Chunk size in bytes (default 10MB)
            
        Yields:
            Chunks of media data
        """
        base_url = item.get('baseUrl')
        if not base_url:
            raise ValueError(f"No baseUrl found for item: {item.get('id')}")
        
        mime_type = item.get('mimeType', '')
        if mime_type.startswith('video/'):
            download_url = f"{base_url}=dv"
        else:
            download_url = f"{base_url}=d"
        
        try:
            response = requests.get(download_url, stream=True)
            response.raise_for_status()
            
            total_size = 0
            for chunk in response.iter_content(chunk_size=chunk_size):
                if chunk:
                    total_size += len(chunk)
                    yield chunk
            
            logger.info(f"Downloaded {item.get('filename')} in chunks ({total_size} bytes total)")
        
        except requests.RequestException as error:
            logger.error(f"Error downloading media item {item.get('filename')}: {error}")
            raise
    
    def get_media_item_metadata(self, item_id: str) -> Dict[str, Any]:
        """Get detailed metadata for a specific media item.
        
        Args:
            item_id: Media item ID
            
        Returns:
            Media item dictionary with full metadata
        """
        try:
            response = self._make_request('GET', f'mediaItems/{item_id}')
            return response
        except requests.RequestException as error:
            logger.error(f"Error getting media item metadata: {error}")
            raise
    
    def normalize_media_item(self, item: Dict[str, Any]) -> Dict[str, Any]:
        """Normalize a media item to a standard format compatible with state manager.
        
        Args:
            item: Raw media item from Google Photos API
            
        Returns:
            Normalized dictionary
        """
        media_metadata = item.get('mediaMetadata', {})
        
        # Extract creation time
        creation_time = media_metadata.get('creationTime', datetime.utcnow().isoformat())
        
        # Build path based on date
        try:
            dt = datetime.fromisoformat(creation_time.replace('Z', '+00:00'))
            path = f"GooglePhotos/{dt.year}/{dt.month:02d}/{item.get('filename', 'unknown')}"
        except:
            path = f"GooglePhotos/{item.get('filename', 'unknown')}"
        
        return {
            'id': item.get('id'),
            'name': item.get('filename', 'unknown'),
            'path': path,
            'mimeType': item.get('mimeType', 'application/octet-stream'),
            'size': None,  # Google Photos API doesn't provide size directly
            'modifiedTime': creation_time,
            'createdTime': creation_time,
            'baseUrl': item.get('baseUrl'),
            'productUrl': item.get('productUrl'),
            'description': item.get('description', ''),
            'mediaMetadata': media_metadata
        }
