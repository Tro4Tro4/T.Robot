"""Public smoke after a release: TLS, login, reserved pages, suspended modules, CSRF, logout.

Only login/logout POSTs succeed; the other POSTs are expected rejections. No data is changed.
Credentials are read from .local/identity-admin.txt ("Nome utente:" / "Password:" lines).
"""
import argparse
import html
import http.cookiejar
import json
import pathlib
import re
import ssl
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
DEFAULT_BASE = 'https://trobot-capodanno.duckdns.org'
DEFAULT_EVENT = 'fc3dfb11-fbcf-46e9-b677-9521e1c07f03'


class NoRedirect(urllib.request.HTTPErrorProcessor):
    def http_response(self, request, response):
        return response
    https_response = http_response


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', default=DEFAULT_BASE)
    parser.add_argument('--event', default=DEFAULT_EVENT)
    args = parser.parse_args()
    cookies = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}),
                                         urllib.request.HTTPSHandler(context=ssl.create_default_context()),
                                         urllib.request.HTTPCookieProcessor(cookies), NoRedirect())

    def call(path, data=None):
        body = urllib.parse.urlencode(data).encode() if data is not None else None
        with opener.open(urllib.request.Request(args.base + path, data=body), timeout=30) as response:
            return response.status, response.headers, response.read()

    def token(body):
        return html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body.decode()).group(1))

    report = {'tls_verified': True}
    assert call('/Events')[0] == 302, 'anonymous access not redirected'
    status, _, body = call('/Access/Login')
    assert status == 200
    lines = (ROOT / '.local/identity-admin.txt').read_text(encoding='utf-8-sig').splitlines()
    credentials = dict(line.split(':', 1) for line in lines if ':' in line)
    status, headers, _ = call('/Access/Login', {'UserName': credentials['Nome utente'].strip(),
                                                'Password': credentials['Password'].strip(),
                                                '__RequestVerificationToken': token(body)})
    assert status == 302, 'login failed'
    assert any(cookie.name == '.TRobot.Identity' and cookie.secure for cookie in cookies)
    assert 'httponly' in headers.get('Set-Cookie', '').lower()
    report['login'] = True

    event = args.event
    routes = {'dashboard': '/?eventId=' + event, 'events': '/Events', 'participants': '/Events/Details/' + event,
              'households': '/Households?eventId=' + event, 'stays': '/Stays?eventId=' + event,
              'lodging': '/Lodging?eventId=' + event, 'accounting': '/Accounting?eventId=' + event,
              'messages': '/Messages?eventId=' + event, 'whatsapp': '/WhatsApp?eventId=' + event,
              'history': '/History?eventId=' + event, 'mealproposals': '/MealProposals?eventId=' + event,
              'mealvoting': '/MealVoting?eventId=' + event}
    for name, route in routes.items():
        status, headers, _ = call(route)
        assert status == 200, name
        assert 'no-store' in headers.get('Cache-Control', ''), name
    report['private_pages'] = len(routes)

    live = json.loads(call('/Messages/Status?eventId=' + event)[2].decode())
    report['gateway_connected_and_group_matches'] = bool(live.get('connectionActive') and live.get('groupMatches'))
    for asset in ('css/site.css', 'js/messages.js'):
        assert call('/' + asset)[2] == (ROOT / 'src/TRobot.Web/wwwroot' / asset).read_bytes(), asset
    report['assets_match_sources'] = True

    report['suspended_get_post_404'] = 0
    for controller in ('Tasks', 'Menu', 'Polls', 'Games'):
        assert call('/' + controller + '?eventId=' + event)[0] == 404, controller
        action = 'Save' if controller in ('Tasks', 'Polls') else 'Create'
        assert call('/' + controller + '/' + action, {'EventId': event})[0] == 404, controller
        report['suspended_get_post_404'] += 2
    report['csrf_rejected'] = 0
    for path, data in (('/Messages/Dispatch', {'EventId': event, 'Confirmed': 'true'}),
                       ('/Messages/Tick', {'EventId': event, 'Confirmed': 'true'}),
                       ('/Messages/Resolve', {'MessageId': event, 'Note': 'x'})):
        assert call(path, data)[0] == 400, path
        report['csrf_rejected'] += 1

    assert call('/Access/Logout', {'__RequestVerificationToken': token(call('/?eventId=' + event)[2])})[0] == 302
    assert call('/Events')[0] == 302
    report['logout_verified'] = True
    print(json.dumps(report))
    if not report['gateway_connected_and_group_matches']:
        raise SystemExit('Site OK, but the panel does not report the gateway connected to the authorized group.')


if __name__ == '__main__':
    main()
