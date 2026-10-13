import test from 'node:test';
import assert from 'node:assert/strict';
import {parseCsv} from '../web/csv.js';
test('CSV inspection handles quoted commas, escaped quotes, CRLF and multiline cells',()=>{assert.deepEqual(parseCsv('\ufeffa,b\r\n"x,y","hello\n""world"""\r\n'),[['a','b'],['x,y','hello\n"world"']]);});
test('CSV inspection rejects malformed quotes',()=>{assert.throws(()=>parseCsv('a,b\n"unclosed'),/Unclosed/);assert.throws(()=>parseCsv('a,b\n"quote"x,1'),/Unexpected/);});
