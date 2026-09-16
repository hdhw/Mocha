const { mochaApiUrl, mochaRecNetUrl } = require('../config');

const REQUEST_TIMEOUT_MS = 8_000;

class MochaApiError extends Error {
  constructor(message, status = null) {
    super(message);
    this.name = 'MochaApiError';
    this.status = status;
  }
}

function recNetLink(fragment = '') {
  const suffix = fragment ? `#${fragment.replace(/^#/, '')}` : '';
  return `${mochaRecNetUrl}/recnet/${suffix}`;
}

async function request(path) {
  const url = new URL(path, `${mochaApiUrl}/`);
  let response;

  try {
    response = await fetch(url, {
      headers: {
        Accept: 'application/json',
        'User-Agent': 'Mocha-Discord-Bot/1.0',
      },
      signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
    });
  } catch (error) {
    if (error?.name === 'TimeoutError') {
      throw new MochaApiError('The Mocha server took too long to respond.');
    }
    throw new MochaApiError('The Mocha server could not be reached.');
  }

  const body = await response.json().catch(() => null);
  if (!response.ok) {
    throw new MochaApiError(
      body?.error || body?.message || `Mocha returned HTTP ${response.status}.`,
      response.status,
    );
  }

  return body;
}

function exactPlayerMatch(players, query) {
  const normalized = String(query).trim().replace(/^@/, '').toLowerCase();
  return players.find((player) => String(player.accountId) === normalized)
    || players.find((player) => player.username?.toLowerCase() === normalized)
    || players.find((player) => player.displayName?.toLowerCase() === normalized)
    || null;
}

function exactRoomMatch(rooms, query) {
  const normalized = String(query).trim().replace(/^\^/, '').toLowerCase();
  return rooms.find((room) => String(room.roomId) === normalized)
    || rooms.find((room) => room.name?.toLowerCase() === normalized)
    || null;
}

async function searchPlayers(query) {
  return request(`/recnet/api/users?search=${encodeURIComponent(query)}`);
}

async function getPlayer(query) {
  if (/^\d+$/.test(String(query).trim())) {
    return request(`/recnet/api/users/${encodeURIComponent(String(query).trim())}`);
  }

  const players = await searchPlayers(query);
  const match = exactPlayerMatch(players, query) || players[0];
  if (!match) return null;
  return request(`/recnet/api/users/${match.accountId}`);
}

async function searchRooms(query = '') {
  return request(`/recnet/api/rooms?search=${encodeURIComponent(query)}`);
}

async function getRoom(query) {
  if (/^\d+$/.test(String(query).trim())) {
    return request(`/recnet/api/rooms/${encodeURIComponent(String(query).trim())}`);
  }

  const rooms = await searchRooms(query);
  return exactRoomMatch(rooms, query) || rooms[0] || null;
}

async function getStatus() {
  return request('/recnet/api/status');
}

async function getEvents() {
  return request('/recnet/api/events');
}

async function getPhotos(take = 25) {
  return request(`/recnet/api/photos/newest?take=${Math.max(1, Math.min(100, take))}`);
}

module.exports = {
  MochaApiError,
  exactPlayerMatch,
  getEvents,
  getPhotos,
  getPlayer,
  getRoom,
  getStatus,
  recNetLink,
  searchPlayers,
  searchRooms,
};
