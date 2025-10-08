"""
Test script for validating the integration components without Google API dependencies.
Tests state manager and Silo client independently.
"""

import sys
import os
import io
from pathlib import Path

# Test imports
print("Testing imports...")
try:
    from state_manager import StateManager
    print("✓ StateManager imported successfully")
except Exception as e:
    print(f"✗ StateManager import failed: {e}")
    sys.exit(1)

try:
    from silo_client import SiloUploadClient, UploadResult, UploadStatus
    print("✓ SiloUploadClient imported successfully")
except Exception as e:
    print(f"✗ SiloUploadClient import failed: {e}")
    sys.exit(1)

print("\n" + "="*60)
print("Testing StateManager")
print("="*60)

# Create a test database
test_db = "test_state.db"
if os.path.exists(test_db):
    os.remove(test_db)

try:
    state = StateManager(test_db)
    print("✓ StateManager initialized")
    
    # Test file synced check
    is_synced = state.is_file_synced("test_file_123", "drive")
    assert is_synced == False, "New file should not be synced"
    print("✓ File sync check works")
    
    # Test marking file as synced
    file_info = {
        'name': 'test.txt',
        'mimeType': 'text/plain',
        'size': 1234,
        'modifiedTime': '2025-10-08T12:00:00Z'
    }
    state.mark_file_synced("test_file_123", "drive", file_info, "silo_id_789", "test-bucket")
    print("✓ File marked as synced")
    
    # Verify it's now synced
    is_synced = state.is_file_synced("test_file_123", "drive")
    assert is_synced == True, "File should be marked as synced"
    print("✓ Sync state persisted correctly")
    
    # Test upload queue
    state.add_to_upload_queue("pending_file_456", "photos", file_info, "test-bucket")
    print("✓ File added to upload queue")
    
    pending = state.get_pending_uploads()
    assert len(pending) > 0, "Should have pending uploads"
    print(f"✓ Retrieved {len(pending)} pending upload(s)")
    
    # Test sync session
    session_id = state.start_sync_session("drive")
    print(f"✓ Sync session started (ID: {session_id})")
    
    stats = {
        'processed': 10,
        'uploaded': 8,
        'failed': 2,
        'bytes_uploaded': 1048576
    }
    state.complete_sync_session(session_id, stats)
    print("✓ Sync session completed")
    
    # Test getting stats
    sync_stats = state.get_sync_stats("drive")
    print(f"✓ Stats retrieved: {sync_stats['synced_files']} synced files")
    
    print("\nStateManager: ALL TESTS PASSED ✓")

except Exception as e:
    print(f"\n✗ StateManager test failed: {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)
finally:
    # Cleanup
    if os.path.exists(test_db):
        os.remove(test_db)
        print(f"Cleaned up test database")

print("\n" + "="*60)
print("Testing SiloUploadClient (without actual uploads)")
print("="*60)

try:
    client = SiloUploadClient(
        server_url="http://localhost:5000",
        bucket="test-bucket",
        max_retries=3
    )
    print("✓ SiloUploadClient initialized")
    
    # Test backoff calculation
    backoff = client._calculate_backoff(0)
    assert backoff >= 0.1, "Initial backoff should be >= 0.1s"
    print(f"✓ Backoff calculation works (retry 0: {backoff:.2f}s)")
    
    backoff = client._calculate_backoff(5)
    assert backoff > 1.0, "Backoff should increase with retries"
    print(f"✓ Exponential backoff works (retry 5: {backoff:.2f}s)")
    
    # Test rate limiting
    assert client._is_rate_limited() == False, "Should not be rate limited initially"
    print("✓ Rate limit check works")
    
    client._set_rate_limit(5)
    assert client._is_rate_limited() == True, "Should be rate limited after setting"
    print("✓ Rate limit setting works")
    
    import time
    time.sleep(1)
    # Still rate limited
    assert client._is_rate_limited() == True, "Should still be rate limited"
    print("✓ Rate limit persists")
    
    # Test stats
    stats = client.get_stats()
    assert 'uploads_attempted' in stats, "Stats should contain uploads_attempted"
    print(f"✓ Stats retrieved: {stats}")
    
    print("\nSiloUploadClient: ALL TESTS PASSED ✓")

except Exception as e:
    print(f"\n✗ SiloUploadClient test failed: {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)

print("\n" + "="*60)
print("ALL COMPONENT TESTS PASSED ✓")
print("="*60)
print("\nNext steps:")
print("1. Install dependencies: pip install -r requirements.txt")
print("2. Get Google OAuth credentials (see README.md)")
print("3. Ensure Silo server is running")
print("4. Run: python main.py --server http://localhost:5000 --bucket files --services drive")
