"""
State management for Google to Silo sync integration.
Tracks synced files, pending uploads, and maintains resumable state.
"""

import sqlite3
import json
import logging
from datetime import datetime
from typing import Optional, List, Dict, Any
from pathlib import Path
from contextlib import contextmanager

logger = logging.getLogger(__name__)


class StateManager:
    """Manages persistent state for the Google sync agent."""
    
    def __init__(self, db_path: str = "google_sync_state.db"):
        """Initialize the state manager with a SQLite database.
        
        Args:
            db_path: Path to the SQLite database file
        """
        self.db_path = db_path
        self._init_db()
    
    def _init_db(self):
        """Initialize the database schema."""
        with self._get_connection() as conn:
            cursor = conn.cursor()
            
            # Table for tracking synced files
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS synced_files (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    google_id TEXT NOT NULL UNIQUE,
                    service TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    file_path TEXT,
                    mime_type TEXT,
                    file_size INTEGER,
                    modified_time TEXT,
                    silo_file_id TEXT,
                    checksum TEXT,
                    bucket TEXT,
                    synced_at TEXT NOT NULL,
                    metadata TEXT,
                    UNIQUE(google_id, service)
                )
            """)
            
            # Table for upload queue (pending/failed uploads)
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS upload_queue (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    google_id TEXT NOT NULL,
                    service TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    file_path TEXT,
                    mime_type TEXT,
                    file_size INTEGER,
                    download_url TEXT,
                    bucket TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'pending',
                    retry_count INTEGER DEFAULT 0,
                    last_error TEXT,
                    created_at TEXT NOT NULL,
                    last_attempt_at TEXT,
                    metadata TEXT,
                    UNIQUE(google_id, service)
                )
            """)
            
            # Table for sync sessions
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS sync_sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    service TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT,
                    status TEXT NOT NULL,
                    files_processed INTEGER DEFAULT 0,
                    files_uploaded INTEGER DEFAULT 0,
                    files_failed INTEGER DEFAULT 0,
                    bytes_uploaded INTEGER DEFAULT 0,
                    error_message TEXT
                )
            """)
            
            # Table for rate limiting state
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS rate_limit_state (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    endpoint TEXT NOT NULL UNIQUE,
                    last_request_at TEXT,
                    request_count INTEGER DEFAULT 0,
                    window_start TEXT,
                    backoff_until TEXT
                )
            """)
            
            conn.commit()
            logger.info(f"Database initialized at {self.db_path}")
    
    @contextmanager
    def _get_connection(self):
        """Context manager for database connections."""
        conn = sqlite3.connect(self.db_path)
        conn.row_factory = sqlite3.Row
        try:
            yield conn
        finally:
            conn.close()
    
    def is_file_synced(self, google_id: str, service: str) -> bool:
        """Check if a file has already been synced.
        
        Args:
            google_id: Google file/item ID
            service: Service name (drive, photos, etc.)
            
        Returns:
            True if file is already synced, False otherwise
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(
                "SELECT COUNT(*) as count FROM synced_files WHERE google_id = ? AND service = ?",
                (google_id, service)
            )
            result = cursor.fetchone()
            return result['count'] > 0
    
    def mark_file_synced(self, google_id: str, service: str, file_info: Dict[str, Any], 
                         silo_file_id: Optional[str] = None, bucket: Optional[str] = None):
        """Mark a file as successfully synced.
        
        Args:
            google_id: Google file/item ID
            service: Service name
            file_info: Dictionary with file metadata
            silo_file_id: Silo file ID (if available)
            bucket: Bucket name where file was uploaded
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                INSERT OR REPLACE INTO synced_files 
                (google_id, service, file_name, file_path, mime_type, file_size, 
                 modified_time, silo_file_id, checksum, bucket, synced_at, metadata)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """, (
                google_id,
                service,
                file_info.get('name', ''),
                file_info.get('path', ''),
                file_info.get('mimeType', ''),
                file_info.get('size', 0),
                file_info.get('modifiedTime', ''),
                silo_file_id,
                file_info.get('md5Checksum', ''),
                bucket,
                datetime.utcnow().isoformat(),
                json.dumps(file_info)
            ))
            conn.commit()
            logger.debug(f"Marked {google_id} as synced")
    
    def add_to_upload_queue(self, google_id: str, service: str, file_info: Dict[str, Any], 
                           bucket: str, download_url: Optional[str] = None):
        """Add a file to the upload queue.
        
        Args:
            google_id: Google file/item ID
            service: Service name
            file_info: File metadata
            bucket: Target bucket name
            download_url: Direct download URL (if available)
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                INSERT OR REPLACE INTO upload_queue
                (google_id, service, file_name, file_path, mime_type, file_size,
                 download_url, bucket, status, created_at, metadata)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'pending', ?, ?)
            """, (
                google_id,
                service,
                file_info.get('name', ''),
                file_info.get('path', ''),
                file_info.get('mimeType', ''),
                file_info.get('size', 0),
                download_url,
                bucket,
                datetime.utcnow().isoformat(),
                json.dumps(file_info)
            ))
            conn.commit()
            logger.debug(f"Added {google_id} to upload queue")
    
    def get_pending_uploads(self, limit: int = 100) -> List[Dict[str, Any]]:
        """Get pending uploads from the queue.
        
        Args:
            limit: Maximum number of items to return
            
        Returns:
            List of pending upload items
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                SELECT * FROM upload_queue 
                WHERE status = 'pending' OR status = 'retrying'
                ORDER BY created_at ASC
                LIMIT ?
            """, (limit,))
            
            rows = cursor.fetchall()
            return [dict(row) for row in rows]
    
    def update_upload_status(self, queue_id: int, status: str, 
                            error_message: Optional[str] = None,
                            silo_file_id: Optional[str] = None):
        """Update the status of an upload queue item.
        
        Args:
            queue_id: Queue item ID
            status: New status (pending, uploading, completed, failed)
            error_message: Error message if failed
            silo_file_id: Silo file ID if upload succeeded
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()
            
            if status == 'completed' and silo_file_id:
                # Move to synced_files table
                cursor.execute("SELECT * FROM upload_queue WHERE id = ?", (queue_id,))
                item = cursor.fetchone()
                if item:
                    file_info = json.loads(item['metadata']) if item['metadata'] else {}
                    self.mark_file_synced(
                        item['google_id'],
                        item['service'],
                        file_info,
                        silo_file_id,
                        item['bucket']
                    )
                    # Remove from queue
                    cursor.execute("DELETE FROM upload_queue WHERE id = ?", (queue_id,))
            else:
                # Update status
                cursor.execute("""
                    UPDATE upload_queue
                    SET status = ?, last_error = ?, last_attempt_at = ?,
                        retry_count = retry_count + 1
                    WHERE id = ?
                """, (status, error_message, datetime.utcnow().isoformat(), queue_id))
            
            conn.commit()
    
    def start_sync_session(self, service: str) -> int:
        """Start a new sync session.
        
        Args:
            service: Service name
            
        Returns:
            Session ID
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                INSERT INTO sync_sessions (service, started_at, status)
                VALUES (?, ?, 'running')
            """, (service, datetime.utcnow().isoformat()))
            conn.commit()
            return cursor.lastrowid
    
    def complete_sync_session(self, session_id: int, stats: Dict[str, Any]):
        """Complete a sync session with statistics.
        
        Args:
            session_id: Session ID
            stats: Statistics dictionary
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                UPDATE sync_sessions
                SET completed_at = ?, status = 'completed',
                    files_processed = ?, files_uploaded = ?, files_failed = ?,
                    bytes_uploaded = ?
                WHERE id = ?
            """, (
                datetime.utcnow().isoformat(),
                stats.get('processed', 0),
                stats.get('uploaded', 0),
                stats.get('failed', 0),
                stats.get('bytes_uploaded', 0),
                session_id
            ))
            conn.commit()
    
    def fail_sync_session(self, session_id: int, error_message: str):
        """Mark a sync session as failed.
        
        Args:
            session_id: Session ID
            error_message: Error message
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                UPDATE sync_sessions
                SET completed_at = ?, status = 'failed', error_message = ?
                WHERE id = ?
            """, (datetime.utcnow().isoformat(), error_message, session_id))
            conn.commit()
    
    def get_sync_stats(self, service: Optional[str] = None) -> Dict[str, Any]:
        """Get sync statistics.
        
        Args:
            service: Optional service filter
            
        Returns:
            Dictionary with statistics
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()
            
            # Total synced files
            if service:
                cursor.execute("SELECT COUNT(*) as count FROM synced_files WHERE service = ?", (service,))
            else:
                cursor.execute("SELECT COUNT(*) as count FROM synced_files")
            synced_count = cursor.fetchone()['count']
            
            # Pending uploads
            if service:
                cursor.execute("SELECT COUNT(*) as count FROM upload_queue WHERE service = ? AND status = 'pending'", (service,))
            else:
                cursor.execute("SELECT COUNT(*) as count FROM upload_queue WHERE status = 'pending'")
            pending_count = cursor.fetchone()['count']
            
            # Failed uploads
            if service:
                cursor.execute("SELECT COUNT(*) as count FROM upload_queue WHERE service = ? AND status = 'failed'", (service,))
            else:
                cursor.execute("SELECT COUNT(*) as count FROM upload_queue WHERE status = 'failed'")
            failed_count = cursor.fetchone()['count']
            
            # Recent sessions
            if service:
                cursor.execute("""
                    SELECT * FROM sync_sessions 
                    WHERE service = ?
                    ORDER BY started_at DESC LIMIT 5
                """, (service,))
            else:
                cursor.execute("""
                    SELECT * FROM sync_sessions 
                    ORDER BY started_at DESC LIMIT 5
                """)
            recent_sessions = [dict(row) for row in cursor.fetchall()]
            
            return {
                'synced_files': synced_count,
                'pending_uploads': pending_count,
                'failed_uploads': failed_count,
                'recent_sessions': recent_sessions
            }
    
    def set_rate_limit_backoff(self, endpoint: str, backoff_seconds: int):
        """Set a backoff period for rate limiting.
        
        Args:
            endpoint: API endpoint
            backoff_seconds: Seconds to back off
        """
        backoff_until = datetime.utcnow().timestamp() + backoff_seconds
        backoff_until_iso = datetime.fromtimestamp(backoff_until).isoformat()
        
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                INSERT OR REPLACE INTO rate_limit_state
                (endpoint, backoff_until, last_request_at)
                VALUES (?, ?, ?)
            """, (endpoint, backoff_until_iso, datetime.utcnow().isoformat()))
            conn.commit()
    
    def is_rate_limited(self, endpoint: str) -> bool:
        """Check if an endpoint is currently rate limited.
        
        Args:
            endpoint: API endpoint
            
        Returns:
            True if rate limited, False otherwise
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                SELECT backoff_until FROM rate_limit_state
                WHERE endpoint = ?
            """, (endpoint,))
            
            row = cursor.fetchone()
            if not row or not row['backoff_until']:
                return False
            
            backoff_until = datetime.fromisoformat(row['backoff_until'])
            return datetime.utcnow() < backoff_until
