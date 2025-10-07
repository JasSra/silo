const http = require('http');

async function testEndpoints() {
    console.log('🚀 Testing advanced search and statistics endpoints...');
    
    // Function to make HTTP requests
    function makeRequest(path, method = 'GET') {
        return new Promise((resolve, reject) => {
            const options = {
                hostname: 'localhost',
                port: 5289,
                path: path,
                method: method,
                headers: {
                    'Content-Type': 'application/json'
                }
            };
            
            const req = http.request(options, (res) => {
                let data = '';
                res.on('data', (chunk) => data += chunk);
                res.on('end', () => {
                    try {
                        const parsed = JSON.parse(data);
                        resolve({ status: res.statusCode, data: parsed });
                    } catch (e) {
                        resolve({ status: res.statusCode, data: data });
                    }
                });
            });
            
            req.on('error', (err) => reject(err));
            req.setTimeout(10000, () => reject(new Error('Request timeout')));
            req.end();
        });
    }
    
    try {
        // Test 1: Statistics endpoint
        console.log('\n📊 Test 1: Statistics endpoint...');
        const statsResponse = await makeRequest('/api/files/statistics');
        console.log('Status:', statsResponse.status);
        console.log('Response:', JSON.stringify(statsResponse.data, null, 2));
        
        if (statsResponse.status === 200 && statsResponse.data.success) {
            console.log('✅ Statistics endpoint working');
        } else {
            console.log('⚠️ Statistics endpoint may have issues');
        }
        
        // Test 2: Basic advanced search
        console.log('\n🔍 Test 2: Basic advanced search...');
        const basicSearchResponse = await makeRequest('/api/files/search/advanced?query=test&limit=10');
        console.log('Status:', basicSearchResponse.status);
        console.log('Response:', JSON.stringify(basicSearchResponse.data, null, 2));
        
        if (basicSearchResponse.status === 200 && basicSearchResponse.data.success) {
            console.log('✅ Basic advanced search working');
        } else {
            console.log('⚠️ Basic advanced search may have issues');
        }
        
        // Test 3: Extension filter search
        console.log('\n📄 Test 3: Extension filter search...');
        const extSearchResponse = await makeRequest('/api/files/search/advanced?extensions=txt,png&limit=5');
        console.log('Status:', extSearchResponse.status);
        console.log('Response:', JSON.stringify(extSearchResponse.data, null, 2));
        
        if (extSearchResponse.status === 200 && extSearchResponse.data.success) {
            console.log('✅ Extension filter search working');
        } else {
            console.log('⚠️ Extension filter search may have issues');
        }
        
        // Test 4: Size range search
        console.log('\n📏 Test 4: Size range search...');
        const sizeSearchResponse = await makeRequest('/api/files/search/advanced?minSize=1&maxSize=10000&limit=5');
        console.log('Status:', sizeSearchResponse.status);
        console.log('Response:', JSON.stringify(sizeSearchResponse.data, null, 2));
        
        if (sizeSearchResponse.status === 200 && sizeSearchResponse.data.success) {
            console.log('✅ Size range search working');
        } else {
            console.log('⚠️ Size range search may have issues');
        }
        
        // Test 5: Wildcard search
        console.log('\n🎯 Test 5: Wildcard search...');
        const wildcardSearchResponse = await makeRequest('/api/files/search/advanced?wildcard=test*&limit=5');
        console.log('Status:', wildcardSearchResponse.status);
        console.log('Response:', JSON.stringify(wildcardSearchResponse.data, null, 2));
        
        if (wildcardSearchResponse.status === 200 && wildcardSearchResponse.data.success) {
            console.log('✅ Wildcard search working');
        } else {
            console.log('⚠️ Wildcard search may have issues');
        }
        
        // Test 6: Combined filters
        console.log('\n🔄 Test 6: Combined filters search...');
        const combinedSearchResponse = await makeRequest('/api/files/search/advanced?query=test&extensions=txt&minSize=1&maxSize=5000&limit=3');
        console.log('Status:', combinedSearchResponse.status);
        console.log('Response:', JSON.stringify(combinedSearchResponse.data, null, 2));
        
        if (combinedSearchResponse.status === 200 && combinedSearchResponse.data.success) {
            console.log('✅ Combined filters search working');
        } else {
            console.log('⚠️ Combined filters search may have issues');
        }
        
        console.log('\n🎉 All endpoint tests completed!');
        
    } catch (error) {
        console.error('❌ Test failed:', error.message);
    }
}

// Run the test
testEndpoints().catch(console.error);