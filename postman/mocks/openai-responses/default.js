// Local fixtures based on postman/openai-responses.openapi.json (OpenAI Responses API). This server models the Responses contract,
// not the model under test; actual PolyPrompt clients and Mux tools execute outside this process.
const http = require('http');
const saved = new Map();
let sequence = 0;
const model = { id: 'test-model', object: 'model', owned_by: 'openai', created: 1700000000 };
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const send = (status, value) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(value)); };
  if (!req.headers.authorization?.startsWith('Bearer ')) return send(401, { error: { message: 'Bearer authentication required' } });
  try {
    if (req.method === 'POST' && url.pathname === '/v1/audio/transcriptions') {
      // Multipart speech to text (PolyPrompt TranscribeAsync). Fields are read loosely; the audio bytes are not decoded.
      const chunks = []; for await (const chunk of req) chunks.push(chunk);
      const form = Buffer.concat(chunks).toString('latin1');
      const field = name => { const m = form.match(new RegExp('name="' + name.replace(/[[\]]/g, '\\$&') + '"\\r\\n\\r\\n([^\\r]*)')); return m ? m[1] : undefined; };
      if (!/name="file"; filename=/.test(form) || !field('model')) return send(400, { error: { message: 'file and model are required' } });
      const format = field('response_format') || 'json';
      if (format === 'text') { res.writeHead(200, { 'Content-Type': 'text/plain' }); return res.end('hello world\n'); }
      if (format === 'verbose_json') return send(200, { task: 'transcribe', language: field('language') || 'english', duration: 2.5, text: 'hello world',
        segments: [{ id: 0, start: 0, end: 0.6, text: ' hello', avg_logprob: -0.21, no_speech_prob: 0.01 }, { id: 1, start: 0.7, end: 1.4, text: ' world', avg_logprob: -0.3, no_speech_prob: 0.02 }],
        ...(form.includes('word') ? { words: [{ word: 'hello', start: 0, end: 0.6 }, { word: 'world', start: 0.7, end: 1.4 }] } : {}) });
      return send(200, { text: 'hello world', usage: { type: 'tokens', input_tokens: 12, output_tokens: 3, total_tokens: 15 } });
    }
    let data = ''; for await (const chunk of req) data += chunk;
    const intercepted = req.body;
    const body = intercepted && typeof intercepted === 'object' ? intercepted : typeof intercepted === 'string' ? JSON.parse(intercepted) : data ? JSON.parse(data) : {};
    if (req.method === 'GET' && url.pathname === '/v1/models') return send(200, { object: 'list', data: [model] });
    if (req.method === 'GET' && /^\/v1\/models\//.test(url.pathname)) return send(200, model);
    if (/^\/v1\/responses\/[^/]+\/input_items$/.test(url.pathname)) {
      const id = decodeURIComponent(url.pathname.split('/')[3]); const record = saved.get(id);
      return record ? send(200, { object: 'list', data: record.input, has_more: false }) : send(404, { error: { message: 'Unknown stored response' } });
    }
    if (/^\/v1\/responses\/[^/]+$/.test(url.pathname) && req.method !== 'POST') {
      const id = decodeURIComponent(url.pathname.split('/')[3]);
      if (!saved.has(id)) return send(404, { error: { message: 'Unknown stored response' } });
      if (req.method === 'DELETE') { saved.delete(id); return send(200, { id, deleted: true, object: 'response' }); }
      return send(200, saved.get(id).response);
    }
    if (req.method === 'POST' && /^\/v1\/responses\/[^/]+\/cancel$/.test(url.pathname)) {
      const id = decodeURIComponent(url.pathname.split('/')[3]); const record = saved.get(id);
      if (!record) return send(404, { error: { message: 'Unknown stored response' } });
      record.response = { ...record.response, status: 'cancelled' }; return send(200, record.response);
    }
    if (req.method === 'POST' && url.pathname === '/v1/responses/input_tokens') return send(200, { object: 'response.input_tokens', input_tokens: 42 });
    if (req.method === 'POST' && url.pathname === '/v1/responses/compact')
      return send(200, { id: 'cmp-test', object: 'response.compaction', model: body.model, output: [{ type: 'compaction', encrypted_content: 'compacted-fixture' }] });
    if (req.method === 'POST' && url.pathname === '/v1/embeddings') {
      const inputs = typeof body.input === 'string' || typeof body.input?.[0] === 'number' ? [body.input] : body.input;
      return send(200, { object: 'list', model: body.model, data: inputs.map((_, index) => ({ index, object: 'embedding', embedding: body.encoding_format === 'base64' ? 'AACAPwAAAEAAAEBA' : [1, 2, 3] })), usage: { prompt_tokens: 2, total_tokens: 2 } });
    }
    if (req.method !== 'POST' || url.pathname !== '/v1/responses') return send(501, { error: { message: 'Outside this local Responses fixture' } });
    if (!body.model || !('input' in body) || 'max_tokens' in body || typeof body.store !== 'boolean') return send(400, { error: { message: 'Invalid Responses request (PolyPrompt always sends store explicitly)' } });
    if (body.previous_response_id && !saved.has(body.previous_response_id)) return send(404, { error: { message: 'Unknown previous_response_id' } });
    const input = Array.isArray(body.input) ? body.input : [{ role: 'user', content: body.input }];
    const results = input.filter(i => i.type === 'function_call_output');
    const images = input.flatMap(i => Array.isArray(i.content) ? i.content : []).filter(c => c.type === 'input_image');
    if (images.some(c => !(c.file_id || /^(data:image\/[a-z+.-]+;base64,|https?:\/\/)/.test(c.image_url || ''))))
      return send(400, { error: { message: 'input_image needs a data URL, an http(s) URL, or a file_id' } });
    const functions = (body.tools || []).filter(t => t.type === 'function');
    const smoke = JSON.stringify(input).includes('openai-console-smoke');
    if (smoke && results.length > 0 && !input.some(i => i.type === 'reasoning' && i.encrypted_content === 'fixture-reasoning'))
      return send(400, { error: { message: 'Encrypted reasoning was not replayed' } });
    const output = [{ type: 'reasoning', id: 'reason-' + sequence, encrypted_content: 'fixture-reasoning', summary: [{ type: 'summary_text', text: 'Use actual tool evidence.' }] }];
    const round = results.length;
    if (smoke && functions.length && round < 2) {
      const name = round === 0 ? 'list_directory' : 'read_file';
      output.push({ type: 'function_call', id: 'fc-' + round, call_id: 'call-' + round, name, arguments: JSON.stringify(round === 0 ? { path: '.' } : { file_path: '.console-smoke.txt' }), status: 'completed' });
    } else {
      const text = smoke && round > 1 ? 'Actual file: ' + results[1].output : images.length ? 'I see ' + images.length + ' image(s).' : 'Hello from the Responses fixture.';
      output.push({ type: 'message', id: 'msg-' + sequence, role: 'assistant', status: 'completed', content: [{ type: 'output_text', text, annotations: [] }] });
    }
    const response = { id: 'resp-' + (++sequence), object: 'response', model: body.model, created_at: 1700000000, status: 'completed', output, store: body.store,
      usage: { input_tokens: 20, output_tokens: 10, total_tokens: 30, input_tokens_details: { cached_tokens: 2, cache_write_tokens: 1 }, output_tokens_details: { reasoning_tokens: 4 } } };
    if (response.store) saved.set(response.id, { response, input });
    if (!body.stream) return send(200, response);
    res.writeHead(200, { 'Content-Type': 'text/event-stream' });
    const event = (type, extra) => res.write('event: ' + type + '\ndata: ' + JSON.stringify({ type, ...extra }) + '\n\n');
    event('response.created', { response: { id: response.id, model: response.model, status: 'in_progress', output: [] } });
    for (let index = 0; index < output.length; index++) {
      const item = output[index];
      event('response.output_item.added', { output_index: index, item: item.type === 'function_call' ? { ...item, arguments: '' } : item });
      if (item.type === 'reasoning') event('response.reasoning_summary_text.delta', { output_index: index, delta: item.summary[0].text });
      if (item.type === 'message') event('response.output_text.delta', { output_index: index, delta: item.content[0].text });
      if (item.type === 'function_call') {
        event('response.function_call_arguments.delta', { output_index: index, item_id: item.id, delta: item.arguments.slice(0, 3) });
        event('response.function_call_arguments.delta', { output_index: index, item_id: item.id, delta: item.arguments.slice(3) });
        event('response.function_call_arguments.done', { output_index: index, item_id: item.id, arguments: item.arguments });
      }
      event('response.output_item.done', { output_index: index, item });
    }
    event('response.completed', { response }); res.end('data: [DONE]\n\n');
  } catch (error) { send(500, { error: { message: String(error.message || error) } }); }
});
server.listen(process.env.PORT || 4913);
