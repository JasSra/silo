"""
Silo upload client with rate limiting, retries, and offline queue support.
Handles uploading files to Silo API with robust error handling.
"""

import io
import logging
import time
import requests
from typing import Optional, Dict, Any, BinaryIO
from pathlib import Path
from datetime import datetime
import threading
from queue import Queue, Empty
from dataclasses import dataclass
from enum import Enum

logger = logging.getLogger(__name__)


class UploadStatus(Enum):
    """Upload status enum."""
    PENDING = "pending"
    UPLOADING = "uploading"
    COMPLETED = "completed"
    FAILED = "failed"
    RATE_LIMITED = "rate_limited"
    RETRYING = "retrying"


@dataclass
class UploadResult:
    """Result of an upload operation."""
    success: bool
    file_id: Optional[str] = None
    file_name: Optional[str] = None
    error_message: Optional[str] = None
    status_code: Optional[int] = None
    retry_after: Optional[int] = None


class SiloUploadClient:
    """Client for uploading files to Silo with retry logic and rate limiting."""
    
    def __init__(self, 
                 server_url: str,
                 bucket: str = "files",
                 max_retries: int = 5,
                 initial_backoff: float = 1.0,
                 max_backoff: float = 300.0,
                 timeout: int = 300):
        """Initialize the Silo upload client.
        
        Args:
            server_url: Base URL of Silo API (e.g., http://localhost:5000)
            bucket: Target bucket name
            max_retries: Maximum number of retry attempts
            initial_backoff: Initial backoff time in seconds
            max_backoff: Maximum backoff time in seconds
            timeout: Request timeout in seconds
        """
        self.server_url = server_url.rstrip('/')
        self.bucket = bucket
        self.max_retries = max_retries
        self.initial_backoff = initial_backoff
        self.max_backoff = max_backoff
        self.timeout = timeout
        
        # Rate limiting state
        self._rate_limit_until = 0
        self._rate_limit_lock = threading.Lock()
        
        # Statistics
        self.stats = {
            'uploads_attempted': 0,
            'uploads_succeeded': 0,
            'uploads_failed': 0,
            'bytes_uploaded': 0,
            'rate_limits_hit': 0
        }
    
    def _calculate_backoff(self, retry_count: int) -> float:
        """Calculate exponential backoff time.
        
        Args:
            retry_count: Current retry attempt number
            
        Returns:
            Backoff time in seconds
        """
        backoff = min(self.initial_backoff * (2 ** retry_count), self.max_backoff)
        # Add jitter (±20%)
        import random
        jitter = backoff * 0.2 * (2 * random.random() - 1)
        return max(0.1, backoff + jitter)  # Ensure at least 0.1s
    
    def _is_rate_limited(self) -> bool:
        """Check if we're currently rate limited.
        
        Returns:
            True if rate limited, False otherwise
        """
        with self._rate_limit_lock:
            return time.time() < self._rate_limit_until
    
    def _set_rate_limit(self, retry_after: int):
        """Set rate limit backoff period.
        
        Args:
            retry_after: Seconds to wait before next request
        """
        with self._rate_limit_lock:
            self._rate_limit_until = time.time() + retry_after
            self.stats['rate_limits_hit'] += 1
            logger.warning(f"Rate limited. Backing off for {retry_after} seconds")
    
    def _wait_if_rate_limited(self):
        """Wait if currently rate limited."""
        if self._is_rate_limited():
            with self._rate_limit_lock:
                wait_time = max(0, self._rate_limit_until - time.time())
            
            if wait_time > 0:
                logger.info(f"Waiting {wait_time:.1f}s due to rate limiting")
                time.sleep(wait_time)
    
    def upload_file(self, 
                   file_stream: BinaryIO,
                   file_name: str,
                   mime_type: str = 'application/octet-stream',
                   metadata: Optional[Dict[str, Any]] = None) -> UploadResult:
        """Upload a file to Silo with retry logic.
        
        Args:
            file_stream: File-like object to upload
            file_name: Name of the file
            mime_type: MIME type of the file
            metadata: Optional metadata dictionary
            
        Returns:
            UploadResult with upload status and details
        """
        self.stats['uploads_attempted'] += 1
        retry_count = 0
        
        while retry_count <= self.max_retries:
            try:
                # Wait if rate limited
                self._wait_if_rate_limited()
                
                # Prepare upload
                file_stream.seek(0)  # Reset stream to beginning
                
                files = {
                    'file': (file_name, file_stream, mime_type)
                }
                
                # Add metadata as form data if provided
                data = {}
                if metadata:
                    for key, value in metadata.items():
                        if isinstance(value, (str, int, float, bool)):
                            data[key] = str(value)
                
                # Make upload request
                url = f"{self.server_url}/api/files/upload"
                logger.info(f"Uploading {file_name} to {url} (attempt {retry_count + 1}/{self.max_retries + 1})")
                
                response = requests.post(
                    url,
                    files=files,
                    data=data,
                    timeout=self.timeout
                )
                
                # Check response
                if response.status_code == 200:
                    # Success
                    result_data = response.json()
                    file_id = result_data.get('FileId') or result_data.get('fileId')
                    
                    # Update statistics
                    file_stream.seek(0, 2)  # Seek to end
                    file_size = file_stream.tell()
                    self.stats['uploads_succeeded'] += 1
                    self.stats['bytes_uploaded'] += file_size
                    
                    logger.info(f"Successfully uploaded {file_name} (File ID: {file_id})")
                    
                    return UploadResult(
                        success=True,
                        file_id=file_id,
                        file_name=file_name,
                        status_code=200
                    )
                
                elif response.status_code == 429:
                    # Rate limited
                    retry_after = int(response.headers.get('Retry-After', 60))
                    self._set_rate_limit(retry_after)
                    
                    logger.warning(f"Rate limited on upload of {file_name}. Retry after {retry_after}s")
                    retry_count += 1
                    
                    if retry_count > self.max_retries:
                        self.stats['uploads_failed'] += 1
                        return UploadResult(
                            success=False,
                            file_name=file_name,
                            error_message="Max retries exceeded due to rate limiting",
                            status_code=429,
                            retry_after=retry_after
                        )
                    
                    continue
                
                elif response.status_code in (500, 502, 503, 504):
                    # Server error - retry with backoff
                    backoff = self._calculate_backoff(retry_count)
                    logger.warning(f"Server error {response.status_code} uploading {file_name}. "
                                 f"Retrying in {backoff:.1f}s")
                    
                    retry_count += 1
                    
                    if retry_count > self.max_retries:
                        self.stats['uploads_failed'] += 1
                        return UploadResult(
                            success=False,
                            file_name=file_name,
                            error_message=f"Server error: {response.status_code}",
                            status_code=response.status_code
                        )
                    
                    time.sleep(backoff)
                    continue
                
                else:
                    # Client error - don't retry
                    error_msg = f"Upload failed with status {response.status_code}: {response.text[:200]}"
                    logger.error(f"Upload failed for {file_name}: {error_msg}")
                    self.stats['uploads_failed'] += 1
                    
                    return UploadResult(
                        success=False,
                        file_name=file_name,
                        error_message=error_msg,
                        status_code=response.status_code
                    )
            
            except requests.ConnectionError as e:
                # Connection error - might be offline or server down
                backoff = self._calculate_backoff(retry_count)
                logger.warning(f"Connection error uploading {file_name}: {e}. Retrying in {backoff:.1f}s")
                
                retry_count += 1
                
                if retry_count > self.max_retries:
                    self.stats['uploads_failed'] += 1
                    return UploadResult(
                        success=False,
                        file_name=file_name,
                        error_message=f"Connection error: {str(e)}"
                    )
                
                time.sleep(backoff)
                continue
            
            except requests.Timeout as e:
                # Timeout - retry with backoff
                backoff = self._calculate_backoff(retry_count)
                logger.warning(f"Timeout uploading {file_name}: {e}. Retrying in {backoff:.1f}s")
                
                retry_count += 1
                
                if retry_count > self.max_retries:
                    self.stats['uploads_failed'] += 1
                    return UploadResult(
                        success=False,
                        file_name=file_name,
                        error_message=f"Timeout error: {str(e)}"
                    )
                
                time.sleep(backoff)
                continue
            
            except Exception as e:
                # Unexpected error
                logger.error(f"Unexpected error uploading {file_name}: {e}", exc_info=True)
                self.stats['uploads_failed'] += 1
                
                return UploadResult(
                    success=False,
                    file_name=file_name,
                    error_message=f"Unexpected error: {str(e)}"
                )
        
        # Should not reach here
        self.stats['uploads_failed'] += 1
        return UploadResult(
            success=False,
            file_name=file_name,
            error_message="Max retries exceeded"
        )
    
    def test_connection(self) -> bool:
        """Test connection to Silo API.
        
        Returns:
            True if connection successful, False otherwise
        """
        try:
            url = f"{self.server_url}/api/files/pipeline/status"
            response = requests.get(url, timeout=10)
            
            if response.status_code == 200:
                logger.info(f"Successfully connected to Silo at {self.server_url}")
                return True
            else:
                logger.warning(f"Connection test failed with status {response.status_code}")
                return False
        
        except Exception as e:
            logger.error(f"Connection test failed: {e}")
            return False
    
    def get_stats(self) -> Dict[str, Any]:
        """Get upload statistics.
        
        Returns:
            Statistics dictionary
        """
        stats = self.stats.copy()
        if stats['uploads_attempted'] > 0:
            stats['success_rate'] = stats['uploads_succeeded'] / stats['uploads_attempted']
        else:
            stats['success_rate'] = 0.0
        
        return stats


class UploadQueue:
    """Background upload queue for handling large batches."""
    
    def __init__(self, 
                 client: SiloUploadClient,
                 num_workers: int = 3,
                 queue_size: int = 1000):
        """Initialize upload queue.
        
        Args:
            client: SiloUploadClient instance
            num_workers: Number of worker threads
            queue_size: Maximum queue size
        """
        self.client = client
        self.num_workers = num_workers
        self.queue = Queue(maxsize=queue_size)
        self.workers = []
        self.running = False
        self.results = []
        self._results_lock = threading.Lock()
    
    def start(self):
        """Start worker threads."""
        if self.running:
            logger.warning("Upload queue already running")
            return
        
        self.running = True
        self.workers = []
        
        for i in range(self.num_workers):
            worker = threading.Thread(target=self._worker, name=f"UploadWorker-{i}")
            worker.daemon = True
            worker.start()
            self.workers.append(worker)
        
        logger.info(f"Started {self.num_workers} upload workers")
    
    def stop(self, wait: bool = True):
        """Stop worker threads.
        
        Args:
            wait: Wait for queue to empty before stopping
        """
        if wait:
            logger.info("Waiting for upload queue to finish...")
            self.queue.join()
        
        self.running = False
        
        # Signal workers to stop
        for _ in self.workers:
            self.queue.put(None)
        
        # Wait for workers
        for worker in self.workers:
            worker.join(timeout=5)
        
        logger.info("Upload queue stopped")
    
    def _worker(self):
        """Worker thread function."""
        while self.running:
            try:
                item = self.queue.get(timeout=1)
                
                if item is None:  # Stop signal
                    self.queue.task_done()
                    break
                
                file_stream, file_name, mime_type, metadata = item
                
                # Perform upload
                result = self.client.upload_file(file_stream, file_name, mime_type, metadata)
                
                # Store result
                with self._results_lock:
                    self.results.append(result)
                
                # Close stream
                if hasattr(file_stream, 'close'):
                    file_stream.close()
                
                self.queue.task_done()
            
            except Empty:
                continue
            except Exception as e:
                logger.error(f"Worker error: {e}", exc_info=True)
                self.queue.task_done()
    
    def add_upload(self, 
                  file_stream: BinaryIO,
                  file_name: str,
                  mime_type: str = 'application/octet-stream',
                  metadata: Optional[Dict[str, Any]] = None):
        """Add a file to the upload queue.
        
        Args:
            file_stream: File-like object
            file_name: File name
            mime_type: MIME type
            metadata: Optional metadata
        """
        if not self.running:
            raise RuntimeError("Upload queue not running. Call start() first.")
        
        self.queue.put((file_stream, file_name, mime_type, metadata))
    
    def get_results(self) -> list:
        """Get upload results.
        
        Returns:
            List of UploadResult objects
        """
        with self._results_lock:
            return self.results.copy()
    
    def clear_results(self):
        """Clear stored results."""
        with self._results_lock:
            self.results.clear()
    
    def pending_count(self) -> int:
        """Get number of pending uploads.
        
        Returns:
            Number of items in queue
        """
        return self.queue.qsize()
