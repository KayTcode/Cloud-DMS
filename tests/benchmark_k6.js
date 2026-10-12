import http from 'k6/http';
import { check, sleep, group } from 'k6';

// k6 run tests/benchmark_k6.js --env BASE_URL=http://localhost:5000
const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

export const options = {
  stages: [
    // 100 users (Mức 1: Baseline)
    { duration: '30s', target: 100 },
    // 1,000 users (Mức 2: Tải trung bình)
    { duration: '1m', target: 1000 },
    // 10,000 users (Mức 3: Cao điểm)
    { duration: '2m', target: 10000 },
    // Cooldown
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<2000'], // 95% request phải phản hồi < 2s
    http_req_failed: ['rate<0.05'],    // Tỷ lệ lỗi < 5%
  },
};

export function setup() {
  // Login as admin or test user to obtain JWT
  const loginRes = http.post(`${BASE_URL}/auth/login`, JSON.stringify({
    email: 'admin@clouddms.com',
    password: 'Admin@123'
  }), {
    headers: { 'Content-Type': 'application/json' }
  });

  if (loginRes.status === 200) {
    const json = loginRes.json();
    return { token: json.accessToken || json.data?.accessToken };
  }
  return { token: null };
}

export default function (data) {
  const authHeaders = {
    'Authorization': `Bearer ${data.token}`,
    'Content-Type': 'application/json'
  };

  group('Endpoint 1: GET /api/files (Phân trang danh sách file)', () => {
    const res = http.get(`${BASE_URL}/api/files?pageNumber=1&pageSize=20`, { headers: authHeaders });
    check(res, {
      'get files status is 200': (r) => r.status === 200,
    });
  });

  group('Endpoint 2: GET /api/admin/users (Phân trang người dùng)', () => {
    const res = http.get(`${BASE_URL}/api/admin/users?pageNumber=1&pageSize=20`, { headers: authHeaders });
    check(res, {
      'get users status is 200 or 403': (r) => r.status === 200 || r.status === 403,
    });
  });

  group('Endpoint 3: POST /api/files/upload (Mô phỏng upload file)', () => {
    const boundary = '----k6FormBoundary7MA4YWxkTrZu0gW';
    const body = `${boundary}\r\n` +
      `Content-Disposition: form-data; name="files"; filename="bench_test.txt"\r\n` +
      `Content-Type: text/plain\r\n\r\n` +
      `Benchmark test file content with unique timestamp ${Date.now()}\r\n` +
      `${boundary}--\r\n`;

    const uploadHeaders = {
      'Authorization': `Bearer ${data.token}`,
      'Content-Type': `multipart/form-data; boundary=${boundary.slice(2)}`
    };

    const res = http.post(`${BASE_URL}/api/files/upload`, body, { headers: uploadHeaders });
    check(res, {
      'upload file returned response': (r) => r.status === 200 || r.status === 400 || r.status === 429,
    });
  });

  sleep(1);
}
