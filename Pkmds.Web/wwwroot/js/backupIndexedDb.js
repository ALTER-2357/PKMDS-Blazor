// Simple IndexedDB helper for PKM backups. Exposes window.pkmdsBackup.* functions.
// Stores blobs under store 'pkm-data' keyed by id and metadata under 'pkm-meta'.
(function () {
    const DB_NAME = 'pkmds-backups';
    const DB_VER = 1;
    const STORE_DATA = 'pkm-data';
    const STORE_META = 'pkm-meta';

    function openDb() {
        return new Promise((res, rej) => {
            const req = indexedDB.open(DB_NAME, DB_VER);
            req.onupgradeneeded = () => {
                const db = req.result;
                if (!db.objectStoreNames.contains(STORE_DATA)) db.createObjectStore(STORE_DATA);
                if (!db.objectStoreNames.contains(STORE_META)) db.createObjectStore(STORE_META);
            };
            req.onsuccess = () => res(req.result);
            req.onerror = () => rej(req.error);
        });
    }

    async function saveBackup(id, byteArrayOrBase64, metaObjOrJson) {
        const db = await openDb();
        // Normalize metadata to object
        const meta = typeof metaObjOrJson === 'string' ? JSON.parse(metaObjOrJson) : metaObjOrJson;

        // Normalize byte array input which can be:
        // - Array (marshalled TypedArray/ArrayBuffer from Blazor)
        // - Uint8Array
        // - base64 string (older marshalling scenarios)
        let uint8;
        if (typeof byteArrayOrBase64 === 'string') {
            const binary = atob(byteArrayOrBase64);
            uint8 = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) uint8[i] = binary.charCodeAt(i);
        } else if (byteArrayOrBase64 instanceof ArrayBuffer) {
            uint8 = new Uint8Array(byteArrayOrBase64);
        } else {
            // Assume array-like
            uint8 = new Uint8Array(byteArrayOrBase64);
        }

        return new Promise((res, rej) => {
            const tx = db.transaction([STORE_DATA, STORE_META], 'readwrite');
            tx.objectStore(STORE_DATA).put(uint8, id);
            tx.objectStore(STORE_META).put(Object.assign({ id }, meta), id);
            tx.oncomplete = () => res();
            tx.onerror = () => rej(tx.error);
        });
    }

    async function listBackupMeta() {
        const db = await openDb();
        return new Promise((res, rej) => {
            const tx = db.transaction(STORE_META, 'readonly');
            const store = tx.objectStore(STORE_META);
            const out = [];
            store.openCursor().onsuccess = function (e) {
                const cursor = e.target.result;
                if (!cursor) {
                    res(JSON.stringify(out));
                    return;
                }
                const meta = cursor.value;
                // include the key in case it's not in value
                meta.id = cursor.key;
                out.push(meta);
                cursor.continue();
            };
            tx.onerror = () => rej(tx.error);
        });
    }

    async function getBackupData(id) {
        const db = await openDb();
        return new Promise((res, rej) => {
            const tx = db.transaction(STORE_DATA, 'readonly');
            const req = tx.objectStore(STORE_DATA).get(id);
            req.onsuccess = () => {
                const value = req.result;
                if (!value) { res(null); return; }
                // Return as Uint8Array -> Blazor will marshal to byte[]
                const uint8 = new Uint8Array(value);
                // Convert to regular array for older Blazor marshalling if needed
                res(uint8);
            };
            req.onerror = () => rej(req.error);
        });
    }

    async function deleteBackup(id) {
        const db = await openDb();
        return new Promise((res, rej) => {
            const tx = db.transaction([STORE_DATA, STORE_META], 'readwrite');
            tx.objectStore(STORE_DATA).delete(id);
            tx.objectStore(STORE_META).delete(id);
            tx.oncomplete = () => res();
            tx.onerror = () => rej(tx.error);
        });
    }

    window.pkmdsBackup = {
        saveBackup: saveBackup,
        listBackupMeta: listBackupMeta,
        getBackupData: getBackupData,
        deleteBackup: deleteBackup
    };
})();
