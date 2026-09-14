#!/usr/bin/env python3
# Zako Code 폰판 — Termux(갤럭시)·iSH(아이폰)에서 AI 가 직접 명령을 실행하며 일하는 터미널 프로그램.
# 표준 라이브러리만 쓴다: 폰에 pip 로 깔 것이 없다.
#
#   python3 zako.py              대화 시작 (처음이면 API 키·보낼 곳·모델을 묻는다)
#   python3 zako.py --selftest   가짜 서버로 명령 실행 흐름을 점검
#
# AI 가 ```bash 블록을 쓰면 실행할지 묻고, 결과를 AI 에게 돌려준다. 명령 없는 답이 올 때까지 반복.

import getpass
import json
import os
import platform
import re
import subprocess
import sys
import urllib.error
import urllib.request

try:
    import readline  # noqa: F401 — 화살표로 이전 입력 불러오기. 없는 환경도 있다
except ImportError:
    pass

VERSION = '1.0.1'   # PC 판·안드로이드 판과 같은 번호
CONF = os.path.join(os.path.expanduser('~'), '.zako', 'config.json')
MAX_STEPS = 20      # 한 질문에 명령을 연달아 돌리는 횟수 상한 — 같은 명령을 끝없이 되풀이하는 모델이 있다
OUT_LIMIT = 4000    # AI 에게 돌려줄 출력 길이. 넘으면 뒤쪽만 보낸다 (에러는 보통 끝에 있다)
FENCE = re.compile(r'```(?:bash|sh|shell)[ \t]*\r?\n(.*?)```', re.S)


def commands(text):
    return [c.strip() for c in FENCE.findall(text) if c.strip()]


def where():
    if 'com.termux' in os.environ.get('PREFIX', ''):
        return '안드로이드 Termux'
    if os.path.exists('/proc/ish'):
        return '아이폰 iSH (Alpine 리눅스)'
    return platform.platform()


def system_prompt():
    return (
        '너는 Zako Code 다. 사용자의 터미널(' + where() + ')에서 명령을 직접 실행하며 일을 대신한다.\n'
        '- 명령이 필요하면 ```bash 코드 블록에 넣어라. 사용자가 허락하면 실제로 실행되고, 결과가 다음 메시지로 온다.\n'
        '- 실행하면 안 되는 예시 코드는 bash 로 표시하지 마라. python, text 처럼 다른 이름을 붙여라.\n'
        '- 한 번에 한 단계씩 실행하고 결과를 보고 다음 명령을 정해라.\n'
        '- 입력을 기다리는 명령(vim, nano, 확인 질문)은 쓰지 마라. 설치 명령에는 -y 를 붙여라.\n'
        '- 일이 끝나면 bash 블록 없이 한국어로 짧게 결과를 알려라.\n'
        '- 시작 폴더: ' + os.getcwd()
    )


def load():
    try:
        with open(CONF, encoding='utf-8') as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def save(conf):
    os.makedirs(os.path.dirname(CONF), exist_ok=True)
    fd = os.open(CONF, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)   # 키가 들어 있다 — 본인만 읽게
    with os.fdopen(fd, 'w', encoding='utf-8') as f:
        json.dump(conf, f, ensure_ascii=False)


def request(conf, path, body=None):
    req = urllib.request.Request(
        conf['root'] + path,
        data=None if body is None else json.dumps(body).encode(),
        headers={'Authorization': 'Bearer ' + conf['key'], 'Content-Type': 'application/json', 'User-Agent': 'ZakoCode'},
    )
    return urllib.request.urlopen(req, timeout=300)


def models(conf):
    with request(conf, '/models') as r:
        return sorted(m['id'] for m in json.load(r).get('data', []))


def chat(conf, messages, out):
    body = {'model': conf['model'], 'messages': messages, 'stream': True, 'stream_options': {'include_usage': True}}
    parts, tokens = [], 0
    with request(conf, '/chat/completions', body) as r:
        for raw in r:
            line = raw.decode('utf-8', 'replace').strip()
            if not line.startswith('data:'):
                continue
            data = line[5:].strip()
            if data == '[DONE]':
                break
            chunk = json.loads(data)
            usage = chunk.get('usage')
            if isinstance(usage, dict) and isinstance(usage.get('total_tokens'), int):
                tokens = usage['total_tokens']
            choices = chunk.get('choices') or []   # 마지막 사용량 조각은 choices 가 비어 있다
            if choices:
                piece = (choices[0].get('delta') or {}).get('content') or ''
                parts.append(piece)
                out(piece)
    return ''.join(parts), tokens


def run(cmd):
    # ponytail: 끝난 뒤 한꺼번에 보여준다. 긴 설치를 실시간으로 보고 싶어지면 Popen 으로 줄마다 흘릴 것
    try:
        p = subprocess.run(cmd, shell=True, stdin=subprocess.DEVNULL, capture_output=True,
                           text=True, errors='replace', timeout=600)
        return p.returncode, p.stdout + p.stderr
    except subprocess.TimeoutExpired:
        return None, '(10분이 지나 멈춤)\n'


def turn(conf, messages, approve, out):
    for _ in range(MAX_STEPS):
        reply, tokens = chat(conf, messages, out)
        out('\n' + (f'(토큰 {tokens:,})\n' if tokens else ''))
        messages.append({'role': 'assistant', 'content': reply})
        cmds = commands(reply)
        if not cmds:
            return
        results = []
        for cmd in cmds:
            if not approve(cmd):
                results.append(f'$ {cmd}\n(사용자가 실행을 거절했다)')
                continue
            code, output = run(cmd)
            out(output if not output or output.endswith('\n') else output + '\n')
            results.append(f'$ {cmd}\n(종료 코드 {code})\n{output[-OUT_LIMIT:]}')
        messages.append({'role': 'user', 'content': '명령 실행 결과:\n\n' + '\n\n'.join(results)})
    out(f'명령을 {MAX_STEPS}번 연달아 돌려서 멈췄습니다. 이어가려면 "계속"이라고 입력하세요.\n')


def asker():
    always = False

    def approve(cmd):
        nonlocal always
        print('\n$ ' + cmd)
        if always:
            return True
        a = input('실행할까요? [y 예 / a 끌 때까지 전부 허용 / Enter 아니오] ').strip().lower()
        always = a in ('a', 'ㅁ')
        return a in ('y', 'ㅛ', 'a', 'ㅁ')   # 한글 자판 그대로 눌러도 되게

    return approve


def pick(conf):
    try:
        names = models(conf)
    except (OSError, ValueError) as e:
        print('모델 목록을 못 받았습니다:', e)
        names = []
    for i, name in enumerate(names, 1):
        print(f'{i:3}. {name}')
    a = input('모델 번호나 이름: ').strip()
    chosen = names[int(a) - 1] if a.isdigit() and 0 < int(a) <= len(names) else a
    if not chosen and not conf.get('model'):
        return pick(conf)
    conf['model'] = chosen or conf['model']
    save(conf)


def setup(conf):
    key = getpass.getpass('API 키 (입력해도 화면에 안 보임): ').strip()
    root = input('보낼 곳 Base URL (NVIDIA: https://integrate.api.nvidia.com/v1): ').strip().rstrip('/')
    conf['key'] = key or conf.get('key', '')
    conf['root'] = root or conf.get('root', '')
    if not conf['key'] or not conf['root']:
        print('API 키와 보낼 곳을 둘 다 넣어야 합니다.')
        return setup(conf)
    save(conf)
    pick(conf)


def write(s):
    sys.stdout.write(s)
    sys.stdout.flush()


def main():
    conf = load()
    if not conf.get('key') or not conf.get('root'):
        setup(conf)
    elif not conf.get('model'):
        pick(conf)
    approve = asker()
    messages = [{'role': 'system', 'content': system_prompt()}]
    print(f'Zako Code {VERSION} · {conf["model"]} · /설정 /모델 /새대화 /종료')

    while True:
        try:
            text = input('\n> ').strip()
        except (EOFError, KeyboardInterrupt):
            print()
            return
        if not text:
            continue
        if text in ('/종료', 'exit', 'quit'):
            return
        if text == '/설정':
            setup(conf)
            continue
        if text == '/모델':
            pick(conf)
            print('모델:', conf['model'])
            continue
        if text == '/새대화':
            messages = messages[:1]
            print('새 대화를 시작합니다.')
            continue

        snapshot = [dict(m) for m in messages]
        if messages[-1]['role'] == 'user':
            # 멈춘 명령 결과 뒤에 이어 말하는 경우 — user 가 두 번 연달아 가면 거부하는 모델이 있다
            messages[-1]['content'] += '\n\n' + text
        else:
            messages.append({'role': 'user', 'content': text})
        try:
            turn(conf, messages, approve, write)
            continue
        except KeyboardInterrupt:
            print('\n(중단)')
        except urllib.error.HTTPError as e:
            print(f'\n서버 오류 {e.code}: {e.read().decode("utf-8", "replace")[:500]}')
        except (OSError, ValueError) as e:
            print('\n연결 실패:', e)
        if len(messages) <= len(snapshot) + 1:   # 아무 명령도 안 돌았으면 방금 입력까지 없던 일로 — 다시 입력하면 된다
            messages = snapshot


def selftest():
    import http.server
    import threading

    seen = []

    class Mock(http.server.BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def send(self, kind, data):
            self.send_response(200)
            self.send_header('Content-Type', kind)
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            self.send('application/json', json.dumps({'data': [{'id': 'b/model'}, {'id': 'a/model'}]}).encode())

        def do_POST(self):
            body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
            seen.append((self.headers['Authorization'], body))
            if body['messages'][-1]['content'].startswith('명령 실행 결과'):
                text = '끝났습니다.'
            else:
                text = '확인해 볼게요.\n```bash\necho zako-ok\n```\n```python\nprint("실행하면 안 됨")\n```'
            events = [{'choices': [{'delta': {'content': text[:5]}}]},
                      {'choices': [{'delta': {'content': text[5:]}}]},
                      {'choices': [], 'usage': {'total_tokens': 42}}]
            stream = ''.join('data: ' + json.dumps(e, ensure_ascii=False) + '\n\n' for e in events) + 'data: [DONE]\n\n'
            self.send('text/event-stream', stream.encode())

    server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Mock)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    conf = {'key': 'test-key', 'root': f'http://127.0.0.1:{server.server_address[1]}/v1', 'model': 'a/model'}
    failed = 0

    def check(name, ok):
        nonlocal failed
        print(('통과  ' if ok else '실패  ') + name)
        failed += not ok

    check('bash·sh 블록만 명령으로 뽑는다',
          commands('```bash\nls\n```\n```python\nx\n```\n```sh\necho hi\n```\n```\nplain\n```') == ['ls', 'echo hi'])
    check('모델 목록을 이름순으로 받는다', models(conf) == ['a/model', 'b/model'])

    shown = []
    msgs = [{'role': 'system', 'content': 's'}, {'role': 'user', 'content': 'hi'}]
    turn(conf, msgs, lambda c: True, shown.append)
    result = seen[1][1]['messages'][-1]['content'] if len(seen) == 2 else ''
    check('허락하면 명령을 실행하고 출력을 AI 에게 돌려준다', 'zako-ok' in result and '종료 코드 0' in result)
    check('python 블록은 실행하지 않는다', result != '' and '실행하면 안 됨' not in result)
    check('키와 사용량 요청(include_usage)을 보낸다',
          seen[0][0] == 'Bearer test-key' and seen[0][1]['stream_options'] == {'include_usage': True})
    check('명령 없는 답이 오면 멈추고 토큰을 보여준다',
          msgs[-1] == {'role': 'assistant', 'content': '끝났습니다.'} and '(토큰 42)' in ''.join(shown))

    seen.clear()
    msgs = [{'role': 'system', 'content': 's'}, {'role': 'user', 'content': 'hi'}]
    turn(conf, msgs, lambda c: False, shown.append)
    result = seen[1][1]['messages'][-1]['content'] if len(seen) == 2 else ''
    check('거절하면 실행하지 않고 거절했다고 알린다', '거절' in result and '종료 코드' not in result)

    server.shutdown()
    print(f'\n{"전부 통과" if not failed else f"{failed}개 실패"}')
    return 1 if failed else 0


if __name__ == '__main__':
    if sys.argv[1:] == ['--selftest']:
        sys.exit(selftest())
    main()
