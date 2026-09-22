from pathlib import Path
import struct, hashlib, json
ROOT=Path(__file__).resolve().parents[1]
FIX=ROOT/'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures'
FIX.mkdir(parents=True, exist_ok=True)

def align(v,a): return (v+a-1)//a*a

def checksum_rgo(data):
    assert len(data)%16==0
    lo=hi=0x1111111111111111
    for off in range(0,len(data)-16,16):
        lo=(lo+int.from_bytes(data[off:off+8],'little'))&((1<<64)-1)
        hi=(hi+int.from_bytes(data[off+8:off+16],'little'))&((1<<64)-1)
    data[-16:-8]=lo.to_bytes(8,'little'); data[-8:]=hi.to_bytes(8,'little')

def make_script():
    size=126_976
    jump_off=0x1E080
    b=bytearray(size)
    struct.pack_into('<H',b,0,0x584D)
    # tables at 0x90 and 0xA0
    struct.pack_into('<IIII',b,0x80,0x10,2,0x20,1)
    jump_count=3
    content=jump_off+jump_count*4
    p=content
    def w16(v):
        nonlocal p; struct.pack_into('<H',b,p,v); p+=2
    # raw command before dialog0
    w16(0x1234); w16(0x5678)
    d0=p-jump_off
    w16(0xFFF0); w16(0); w16(1); w16(0xFFFF); w16(3); w16(5)
    d0_term=p-jump_off; w16(0xFFFB)
    w16(0xA001)
    d1=p-jump_off
    w16(0xFFF0); w16(1); w16(2); w16(0xFFFF); w16(4); w16(6); w16(0xFFFD)
    w16(0xA002)
    choice=p-jump_off
    w16(3); w16(4); w16(0xFFFF)
    w16(0xB001); w16(0xB002)
    struct.pack_into('<II',b,0x90,d0,d1)
    struct.pack_into('<I',b,0xA0,1)
    struct.pack_into('<7I',b,0xA4,choice,0,0,0,0,0,0)
    struct.pack_into('<3I',b,jump_off,jump_count*4,content-jump_off,d0_term)
    checksum_rgo(b)
    return bytes(b)

# UTF generic builder matching CRI spec, independent implementation.
SIZE={0:1,1:1,2:2,3:2,4:4,5:4,6:8,7:8,8:4,10:4,11:8}
def be_num(v,n): return int(v).to_bytes(n,'big')
def build_utf(name, columns, rows):
    # columns: dict name,type,storage,value(optional), storage 0x10/0x30/0x50
    strings=bytearray(); soff={}
    def adds(s):
        if s in soff:return soff[s]
        o=len(strings); strings.extend(s.encode('utf-8')+b'\0'); soff[s]=o; return o
    tname=adds(name)
    names=[adds(c['name']) for c in columns]
    for ci,c in enumerate(columns):
        if c['type']==10:
            if c['storage']==0x30: adds(c.get('value',''))
            elif c['storage']==0x50:
                for r in rows:adds(r[ci])
    data=bytearray(); doffs={}
    def addd(key,val):
        while len(data)%4:data.append(0)
        o=len(data); data.extend(val); doffs[key]=o
    for ci,c in enumerate(columns):
        if c['type']==11:
            if c['storage']==0x30:addd(('c',ci),c.get('value',b''))
            elif c['storage']==0x50:
                for ri,r in enumerate(rows):addd(('r',ri,ci),r[ci])
    defs=sum(5+(SIZE[c['type']] if c['storage']==0x30 else 0) for c in columns)
    rowlen=sum(SIZE[c['type']] for c in columns if c['storage']==0x50)
    ro=align(32+defs,8); so=align(ro+rowlen*len(rows),8); do=align(so+len(strings),8); total=align(do+len(data),8)
    out=bytearray(total); out[:4]=b'@UTF'
    struct.pack_into('>IIIIIHHI',out,4,total-8,ro-8,so-8,do-8,tname,len(columns),rowlen,len(rows))
    p=32
    def putval(p,typ,val,key):
        if typ in (0,1): out[p]=int(val); return p+1
        if typ in (2,3): out[p:p+2]=be_num(val,2); return p+2
        if typ in (4,5): out[p:p+4]=be_num(val,4); return p+4
        if typ in (6,7): out[p:p+8]=be_num(val,8); return p+8
        if typ==8: out[p:p+4]=struct.pack('>f',val); return p+4
        if typ==10: out[p:p+4]=be_num(adds(val),4); return p+4
        if typ==11:
            blob=val; out[p:p+4]=be_num(doffs[key],4); out[p+4:p+8]=be_num(len(blob),4); return p+8
        raise ValueError(typ)
    for ci,c in enumerate(columns):
        out[p]=c['storage']|c['type']; p+=1; out[p:p+4]=be_num(names[ci],4);p+=4
        if c['storage']==0x30:p=putval(p,c['type'],c.get('value',0),('c',ci))
    for ri,r in enumerate(rows):
        p=ro+ri*rowlen
        for ci,c in enumerate(columns):
            if c['storage']==0x50:p=putval(p,c['type'],r[ci],('r',ri,ci))
    out[so:so+len(strings)]=strings; out[do:do+len(data)]=data
    return bytes(out)

def enc_utf(t):
    key=0x655f; out=bytearray(t)
    for i in range(len(out)):
        out[i]^=key&0xff; key=(key*0x4115)&0xffffffff
    return bytes(out)
def packet(table,encrypted=True):
    t=enc_utf(table) if encrypted else table
    return struct.pack('<IQ',0,len(t))+t

def crilayla(body):
    raw=bytes((i*7)&255 for i in range(256))
    bits=''.join('0'+format(x,'08b') for x in body[::-1])
    bits += '0'*((-len(bits))%8)
    readbytes=bytes(int(bits[i:i+8],2) for i in range(0,len(bits),8))
    compressed=readbytes[::-1]
    return b'CRILAYLA'+struct.pack('<II',len(body),len(compressed))+compressed+raw, raw+body

def make_cpk(script):
    comp, extracted=crilayla(b'CRILAYLA-INDEPENDENT-FIXTURE')
    dl_cols=[{'name':'ID','type':2,'storage':0x50},{'name':'FileSize','type':2,'storage':0x50},{'name':'ExtractSize','type':2,'storage':0x50}]
    dh_cols=[{'name':'ID','type':2,'storage':0x50},{'name':'FileSize','type':4,'storage':0x50},{'name':'ExtractSize','type':4,'storage':0x50}]
    dl=build_utf('CpkItocL',dl_cols,[[1,len(comp),len(extracted)]])
    dh=build_utf('CpkItocH',dh_cols,[[0,len(script),len(script)]])
    it_cols=[{'name':'FilesL','type':4,'storage':0x30,'value':1},{'name':'FilesH','type':4,'storage':0x30,'value':1},{'name':'DataL','type':11,'storage':0x30,'value':dl},{'name':'DataH','type':11,'storage':0x30,'value':dh}]
    it=build_utf('CpkItocInfo',it_cols,[[]])
    itchunk=b'ITOC'+packet(it,True)
    itoff=0x800; itsize=len(itchunk); content=align(itoff+itsize,0x800)
    packed_sum=len(script)+len(comp); extract_sum=len(script)+len(extracted)
    hcols=[
      {'name':'ContentOffset','type':6,'storage':0x30,'value':content},
      {'name':'ContentSize','type':6,'storage':0x30,'value':packed_sum},
      {'name':'ItocOffset','type':6,'storage':0x30,'value':itoff},
      {'name':'ItocSize','type':6,'storage':0x30,'value':itsize},
      {'name':'EnabledPackedSize','type':6,'storage':0x30,'value':packed_sum},
      {'name':'EnabledDataSize','type':6,'storage':0x30,'value':extract_sum},
      {'name':'Files','type':4,'storage':0x30,'value':2},
      {'name':'Align','type':2,'storage':0x30,'value':0x800},
    ]
    ht=build_utf('CpkHeader',hcols,[[]]); hp=packet(ht,True)
    p=content; positions=[]
    for blob in (script,comp): positions.append(p); p+=len(blob); p=align(p,0x800)
    length=positions[-1]+len(comp)
    out=bytearray(length); out[:4]=b'CPK ';out[4:4+len(hp)]=hp;out[0x7fa:0x800]=b'(c)CRI';out[itoff:itoff+len(itchunk)]=itchunk
    out[positions[0]:positions[0]+len(script)]=script;out[positions[1]:positions[1]+len(comp)]=comp
    return bytes(out),extracted

RUSSIAN = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя"

def make_glyph_map():
    base = [' ', 'A', 'B', 'А', 'Б', 'а', 'б', '!', '?']
    base.extend(ch for ch in RUSSIAN if ch not in {'А', 'Б', 'а', 'б'})
    return base

def pack_lt_glyph(levels, width, reserved=0, unused_pixel_bits=None):
    assert len(levels) == 18 * 18
    if unused_pixel_bits is None:
        unused_pixel_bits = [0] * 18
    assert len(unused_pixel_bits) == 18
    out = bytearray(92)
    for y in range(18):
        for x in range(18):
            value = levels[y * 18 + x]
            assert 0 <= value <= 3
            off = y * 5 + x // 4
            out[off] |= value << ((x % 4) * 2)
        assert unused_pixel_bits[y] & 0x0F == 0
        out[y * 5 + 4] |= unused_pixel_bits[y]
    out[90] = width
    out[91] = reserved
    return bytes(out)

def simple_glyph(seed):
    levels = [0] * (18 * 18)
    for y in range(3, 15):
        for x in range(5, 13):
            if y in (3, 14) or x in (5, 12) or ((seed >> ((x + y) % 8)) & 1 and (x + y) % 5 == 0):
                levels[y * 18 + x] = 3
    return levels

def make_lt_font(glyph_map):
    records = bytearray()
    for index, text in enumerate(glyph_map):
        if index == 0:
            levels, width = [0] * (18 * 18), 4
        elif text in {'A', 'B', '!', '?'}:
            levels, width = simple_glyph(ord(text)), 9
        else:
            levels, width = [0] * (18 * 18), 9
        unused = [0] * 18
        reserved = 0
        if index == 1:
            unused[0] = 0xA0
        if index == 2:
            reserved = 0x7B
        if text == 'А':
            unused[1] = 0x50
            reserved = 0x5A
        records += pack_lt_glyph(levels, width, reserved, unused)
    total = align(len(records) + 16, 2048)
    out = bytearray(total)
    out[:len(records)] = records
    if len(records) < total - 16:
        out[len(records)] = 0xCC
    checksum_rgo(out)
    return bytes(out)

def make_bdf():
    lines = [
        'STARTFONT 2.1',
        'FONT -LuckyStar-Oracle-Medium-R-Normal--12-120-75-75-C-90-ISO10646-1',
        'SIZE 12 75 75',
        'FONTBOUNDINGBOX 8 12 0 -2',
        'STARTPROPERTIES 2',
        'FONT_ASCENT 10',
        'FONT_DESCENT 2',
        'ENDPROPERTIES',
        f'CHARS {len(RUSSIAN)}',
    ]
    for ch in RUSSIAN:
        cp = ord(ch)
        lines += [
            f'STARTCHAR U{cp:04X}',
            f'ENCODING {cp}',
            'SWIDTH 750 0',
            'DWIDTH 9 0',
            'BBX 8 12 0 -2',
            'BITMAP',
        ]
        for row in range(12):
            if row in (0, 11):
                value = 0x7E
            else:
                value = 0x42 | (((cp >> (row % 8)) ^ (cp * (row + 3))) & 0x3C)
            lines.append(f'{value:02X}')
        lines.append('ENDCHAR')
    lines.append('ENDFONT')
    return ('\n'.join(lines) + '\n').encode('ascii')

def parse_bdf_rows(data):
    lines = data.decode('ascii').splitlines()
    glyphs = {}
    current = None
    for line in lines:
        if line.startswith('STARTCHAR '):
            current = {'encoding': None, 'advance': None, 'box': None, 'rows': [], 'bitmap': False}
        elif current is not None and line.startswith('ENCODING '):
            current['encoding'] = int(line.split()[1])
        elif current is not None and line.startswith('DWIDTH '):
            current['advance'] = int(line.split()[1])
        elif current is not None and line.startswith('BBX '):
            _, w, h, xo, yo = line.split()
            current['box'] = (int(w), int(h), int(xo), int(yo))
        elif current is not None and line == 'BITMAP':
            current['bitmap'] = True
        elif current is not None and line == 'ENDCHAR':
            glyphs[current['encoding']] = current
            current = None
        elif current is not None and current['bitmap']:
            current['rows'].append(int(line, 16))
    return glyphs

def unpack_lt_font(data, glyph_count):
    assert len(data) >= glyph_count * 92 + 16
    records = []
    for index in range(glyph_count):
        record = data[index*92:(index+1)*92]
        levels = [0] * (18 * 18)
        for y in range(18):
            for x in range(18):
                levels[y*18+x] = (record[y*5+x//4] >> ((x%4)*2)) & 3
        unused = [record[y * 5 + 4] & 0xF0 for y in range(18)]
        records.append([levels, record[90], record[91], unused])
    padding = data[glyph_count*92:-16]
    return records, padding

def rasterize_bdf_glyph(glyph, baseline=15, intensity=3, x_shift=0, y_shift=0):
    width, height, xoff, yoff = glyph['box']
    advance = glyph['advance'] if glyph['advance'] > 0 else width
    horizontal_origin = (18 - advance) // 2 + xoff + x_shift
    levels = [0] * (18 * 18)
    clipped = 0
    assert len(glyph['rows']) == height
    for source_y, row_bits in enumerate(glyph['rows']):
        coordinate_y = yoff + height - 1 - source_y
        target_y = baseline - 1 - coordinate_y + y_shift
        for source_x in range(width):
            if not (row_bits & (0x80 >> source_x)):
                continue
            target_x = horizontal_origin + source_x
            if not (0 <= target_x < 18 and 0 <= target_y < 18):
                clipped += 1
                continue
            levels[target_y*18+target_x] = intensity
    return levels, max(1, min(18, advance)), clipped

def patch_lt_font(source, glyph_map, bdf):
    records, padding = unpack_lt_font(source, len(glyph_map))
    glyphs = parse_bdf_rows(bdf)
    for ch in RUSSIAN:
        index = glyph_map.index(ch)
        levels, advance, clipped = rasterize_bdf_glyph(glyphs[ord(ch)])
        assert clipped == 0
        records[index] = [levels, advance, records[index][2], records[index][3]]
    out = bytearray(len(source))
    pos = 0
    for levels, width, reserved, unused in records:
        packed = pack_lt_glyph(levels, width, reserved, unused)
        out[pos:pos+92] = packed
        pos += 92
    out[pos:pos+len(padding)] = padding
    checksum_rgo(out)
    return bytes(out)

def render_lt_atlas(data, glyph_count, columns=16, scale=2, gutter=1):
    records, _ = unpack_lt_font(data, glyph_count)
    rows = (glyph_count + columns - 1) // columns
    cell_w = 18 * scale + gutter
    cell_h = 18 * scale + gutter
    width = columns * cell_w + gutter
    height = rows * cell_h + gutter
    rgba = bytearray(width * height * 4)
    for i in range(width * height):
        rgba[i*4+3] = 255
    for index, (levels, _, _, _) in enumerate(records):
        origin_x = gutter + (index % columns) * cell_w
        origin_y = gutter + (index // columns) * cell_h
        for y in range(18):
            for x in range(18):
                value = levels[y*18+x] * 85
                for sy in range(scale):
                    for sx in range(scale):
                        tx = origin_x + x*scale + sx
                        ty = origin_y + y*scale + sy
                        off = (ty*width+tx)*4
                        rgba[off:off+4] = bytes((value,value,value,255))
    return width, height, bytes(rgba)


ISO_SECTOR = 2048

def both16(value):
    return struct.pack('<H', value) + struct.pack('>H', value)

def both32(value):
    return struct.pack('<I', value) + struct.pack('>I', value)

def make_sfo():
    values = [
        ('CATEGORY', 0x0204, b'UG\0'),
        ('DISC_ID', 0x0204, b'ULJM05752\0'),
        ('TITLE', 0x0204, 'Lucky Star Oracle Fixture'.encode('utf-8') + b'\0'),
    ]
    keys = bytearray()
    key_offsets = []
    for key, _, _ in values:
        key_offsets.append(len(keys))
        keys += key.encode('ascii') + b'\0'
    key_table = 20 + len(values) * 16
    data_table = align(key_table + len(keys), 4)
    data = bytearray()
    entries = bytearray()
    for (key, fmt, value), key_off in zip(values, key_offsets):
        while len(data) % 4:
            data.append(0)
        data_off = len(data)
        max_len = align(len(value), 4)
        entries += struct.pack('<HHIII', key_off, fmt, len(value), max_len, data_off)
        data += value
        data += b'\0' * (max_len - len(value))
    out = bytearray(data_table + len(data))
    struct.pack_into('<4sIIII', out, 0, b'\0PSF', 0x00000101, key_table, data_table, len(values))
    out[20:20+len(entries)] = entries
    out[key_table:key_table+len(keys)] = keys
    out[data_table:] = data
    return bytes(out)

def iso_record(lba, length, flags, identifier):
    if isinstance(identifier, int):
        ident = bytes([identifier])
    else:
        ident = identifier.encode('ascii')
    record_len = 33 + len(ident) + (1 if len(ident) % 2 == 0 else 0)
    out = bytearray(record_len)
    out[0] = record_len
    out[1] = 0
    out[2:10] = both32(lba)
    out[10:18] = both32(length)
    out[18:25] = bytes([126, 9, 7, 0, 0, 0, 0])
    out[25] = flags
    out[26] = 0
    out[27] = 0
    out[28:32] = both16(1)
    out[32] = len(ident)
    out[33:33+len(ident)] = ident
    return bytes(out)

def write_iso_directory(image, lba, records):
    payload = b''.join(records)
    assert len(payload) <= ISO_SECTOR
    start = lba * ISO_SECTOR
    image[start:start+len(payload)] = payload

def make_iso(cpk, lt_font):
    param = make_sfo()
    files = {
        'PSP_GAME/PARAM.SFO': param,
        'PSP_GAME/SYSDIR/EBOOT.BIN': b'\x7fELF' + bytes(range(64)),
        'PSP_GAME/SYSDIR/BOOT.BIN': bytes(96),
        'PSP_GAME/USRDIR/DATA/sc.cpk': cpk,
        'PSP_GAME/USRDIR/DATA/lt.bin': lt_font,
        'PSP_GAME/USRDIR/DATA/union.cpk': b'CPK UNION ORACLE\n',
        'PSP_GAME/USRDIR/DATA/pr.bin': b'PR BIN ORACLE\n',
        'PSP_GAME/USRDIR/DATA/titlein.pmf': b'PSMF0015' + bytes(range(32)),
    }
    multi = bytes((i * 19 + 7) & 0xff for i in range(3000))

    directory_lbas = {'':20, 'PSP_GAME':21, 'PSP_GAME/SYSDIR':22, 'PSP_GAME/USRDIR':23, 'PSP_GAME/USRDIR/DATA':24}
    next_lba = 30
    allocations = {}
    for path, blob in files.items():
        allocations[path] = [(next_lba, len(blob), blob)]
        next_lba += max(1, align(len(blob), ISO_SECTOR) // ISO_SECTOR)
    multi_first = multi[:1024]
    multi_second = multi[1024:]
    allocations['PSP_GAME/USRDIR/DATA/MULTI.BIN'] = [
        (next_lba, len(multi_first), multi_first),
        (next_lba + 1, len(multi_second), multi_second),
    ]
    next_lba += 2
    volume_blocks = next_lba + 1
    image = bytearray(volume_blocks * ISO_SECTOR)

    for parts in allocations.values():
        for lba, length, blob in parts:
            start = lba * ISO_SECTOR
            image[start:start+length] = blob

    root = directory_lbas['']
    psp = directory_lbas['PSP_GAME']
    sysdir = directory_lbas['PSP_GAME/SYSDIR']
    usrdir = directory_lbas['PSP_GAME/USRDIR']
    data = directory_lbas['PSP_GAME/USRDIR/DATA']

    write_iso_directory(image, root, [
        iso_record(root, ISO_SECTOR, 0x02, 0),
        iso_record(root, ISO_SECTOR, 0x02, 1),
        iso_record(psp, ISO_SECTOR, 0x02, 'PSP_GAME'),
    ])
    write_iso_directory(image, psp, [
        iso_record(psp, ISO_SECTOR, 0x02, 0),
        iso_record(root, ISO_SECTOR, 0x02, 1),
        iso_record(allocations['PSP_GAME/PARAM.SFO'][0][0], len(param), 0, 'PARAM.SFO;1'),
        iso_record(sysdir, ISO_SECTOR, 0x02, 'SYSDIR'),
        iso_record(usrdir, ISO_SECTOR, 0x02, 'USRDIR'),
    ])
    write_iso_directory(image, sysdir, [
        iso_record(sysdir, ISO_SECTOR, 0x02, 0),
        iso_record(psp, ISO_SECTOR, 0x02, 1),
        iso_record(allocations['PSP_GAME/SYSDIR/EBOOT.BIN'][0][0], len(files['PSP_GAME/SYSDIR/EBOOT.BIN']), 0, 'EBOOT.BIN;1'),
        iso_record(allocations['PSP_GAME/SYSDIR/BOOT.BIN'][0][0], len(files['PSP_GAME/SYSDIR/BOOT.BIN']), 0, 'BOOT.BIN;1'),
    ])
    write_iso_directory(image, usrdir, [
        iso_record(usrdir, ISO_SECTOR, 0x02, 0),
        iso_record(psp, ISO_SECTOR, 0x02, 1),
        iso_record(data, ISO_SECTOR, 0x02, 'DATA'),
    ])
    data_records = [
        iso_record(data, ISO_SECTOR, 0x02, 0),
        iso_record(usrdir, ISO_SECTOR, 0x02, 1),
    ]
    for path in [
        'PSP_GAME/USRDIR/DATA/sc.cpk',
        'PSP_GAME/USRDIR/DATA/lt.bin',
        'PSP_GAME/USRDIR/DATA/union.cpk',
        'PSP_GAME/USRDIR/DATA/pr.bin',
        'PSP_GAME/USRDIR/DATA/titlein.pmf',
    ]:
        name = path.rsplit('/', 1)[1].upper() + ';1'
        data_records.append(iso_record(allocations[path][0][0], len(files[path]), 0, name))
    data_records.append(iso_record(allocations['PSP_GAME/USRDIR/DATA/MULTI.BIN'][0][0], len(multi_first), 0x80, 'MULTI.BIN;1'))
    data_records.append(iso_record(allocations['PSP_GAME/USRDIR/DATA/MULTI.BIN'][1][0], len(multi_second), 0x00, 'MULTI.BIN;1'))
    write_iso_directory(image, data, data_records)

    pvd = bytearray(ISO_SECTOR)
    pvd[0] = 1
    pvd[1:6] = b'CD001'
    pvd[6] = 1
    pvd[8:40] = b'LUCKY STAR ORACLE'.ljust(32, b' ')
    pvd[40:72] = b'LSPTOOL_ORACLE'.ljust(32, b' ')
    pvd[80:88] = both32(volume_blocks)
    pvd[120:124] = both16(1)
    pvd[124:128] = both16(1)
    pvd[128:132] = both16(ISO_SECTOR)
    pvd[156:156+34] = iso_record(root, ISO_SECTOR, 0x02, 0)
    pvd[881] = 1
    image[16*ISO_SECTOR:17*ISO_SECTOR] = pvd
    term = bytearray(ISO_SECTOR)
    term[0] = 255
    term[1:6] = b'CD001'
    term[6] = 1
    image[17*ISO_SECTOR:18*ISO_SECTOR] = term

    expected_files = dict(files)
    expected_files['PSP_GAME/USRDIR/DATA/MULTI.BIN'] = multi
    return bytes(image), expected_files

def find_iso_record_offset(image, raw_identifier):
    encoded = raw_identifier.encode('ascii')
    identifier_offset = image.find(encoded)
    assert identifier_offset >= 33, raw_identifier
    record_offset = identifier_offset - 33
    record_length = image[record_offset]
    assert record_length >= 34
    assert image[record_offset + 32] == len(encoded)
    assert image[record_offset + 33:record_offset + 33 + len(encoded)] == encoded
    return record_offset

def patch_iso_oracle(source, replacements):
    out = bytearray(source)
    while len(out) % ISO_SECTOR:
        out.append(0)
    replacement_meta = {}
    for iso_path, raw_identifier, payload in sorted(replacements, key=lambda item: item[0].lower()):
        output_offset = len(out)
        output_lba = output_offset // ISO_SECTOR
        out += payload
        while len(out) % ISO_SECTOR:
            out.append(0)
        record_offset = find_iso_record_offset(out, raw_identifier)
        out[record_offset + 2:record_offset + 10] = both32(output_lba)
        out[record_offset + 10:record_offset + 18] = both32(len(payload))
        replacement_meta[iso_path] = {
            'size': len(payload),
            'sha256': hashlib.sha256(payload).hexdigest(),
            'logicalBlock': output_lba,
            'directoryRecordOffset': record_offset,
        }
    blocks = len(out) // ISO_SECTOR
    out[16 * ISO_SECTOR + 80:16 * ISO_SECTOR + 88] = both32(blocks)
    return bytes(out), replacement_meta

script=make_script(); cpk,expected=make_cpk(script)
glyph_map=make_glyph_map()
lt_font=make_lt_font(glyph_map)
bdf=make_bdf()
patched_lt_font=patch_lt_font(lt_font,glyph_map,bdf)
atlas_width,atlas_height,atlas_rgba=render_lt_atlas(patched_lt_font,len(glyph_map))
iso_image,iso_files=make_iso(cpk,lt_font)
replacement_lt=patched_lt_font
replacement_sc=cpk + bytes((index * 37 + 11) & 0xff for index in range(4097))
patched_iso,patched_iso_replacements=patch_iso_oracle(iso_image,[
    ('PSP_GAME/USRDIR/DATA/lt.bin','LT.BIN;1',replacement_lt),
    ('PSP_GAME/USRDIR/DATA/sc.cpk','SC.CPK;1',replacement_sc),
])
(FIX/'reference-script.bin').write_bytes(script)
(FIX/'reference.cpk').write_bytes(cpk)
(FIX/'crilayla-extracted.bin').write_bytes(expected)
(FIX/'glyph-map.txt').write_text('\n'.join(glyph_map)+'\n',encoding='utf-8',newline='\n')
(FIX/'reference-lt.bin').write_bytes(lt_font)
(FIX/'reference-font.bdf').write_bytes(bdf)
(FIX/'reference-lt-russian.bin').write_bytes(patched_lt_font)
(FIX/'reference-lt-russian-atlas.rgba').write_bytes(atlas_rgba)
(FIX/'reference.iso').write_bytes(iso_image)
(FIX/'iso-replacement-lt.bin').write_bytes(replacement_lt)
(FIX/'iso-replacement-sc.cpk').write_bytes(replacement_sc)
(FIX/'reference-patched.iso').write_bytes(patched_iso)
iso_patch_manifest={
    'schema':'lucky-star-psp.iso-patch-manifest.v1',
    'expectedSourceSha256':hashlib.sha256(iso_image).hexdigest(),
    'replacements':[
        {
            'isoPath':'PSP_GAME/USRDIR/DATA/sc.cpk',
            'sourceFile':'iso-replacement-sc.cpk',
            'expectedOriginalSize':len(cpk),
            'expectedOriginalSha256':hashlib.sha256(cpk).hexdigest(),
            'expectedReplacementSize':len(replacement_sc),
            'expectedReplacementSha256':hashlib.sha256(replacement_sc).hexdigest(),
        },
        {
            'isoPath':'PSP_GAME/USRDIR/DATA/lt.bin',
            'sourceFile':'iso-replacement-lt.bin',
            'expectedOriginalSize':len(lt_font),
            'expectedOriginalSha256':hashlib.sha256(lt_font).hexdigest(),
            'expectedReplacementSize':len(replacement_lt),
            'expectedReplacementSha256':hashlib.sha256(replacement_lt).hexdigest(),
        },
    ],
}
(FIX/'iso-patch-manifest.json').write_text(json.dumps(iso_patch_manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
meta={
    'scriptSha256':hashlib.sha256(script).hexdigest(),
    'cpkSha256':hashlib.sha256(cpk).hexdigest(),
    'scriptLength':len(script),
    'cpkLength':len(cpk),
    'crilaylaExtractedSha256':hashlib.sha256(expected).hexdigest(),
    'glyphMapSha256':hashlib.sha256(('\n'.join(glyph_map)+'\n').encode('utf-8')).hexdigest(),
    'fontSha256':hashlib.sha256(lt_font).hexdigest(),
    'fontLength':len(lt_font),
    'fontGlyphCount':len(glyph_map),
    'bdfSha256':hashlib.sha256(bdf).hexdigest(),
    'bdfGlyphCount':len(RUSSIAN),
    'patchedFontSha256':hashlib.sha256(patched_lt_font).hexdigest(),
    'atlasWidth':atlas_width,
    'atlasHeight':atlas_height,
    'atlasRgbaSha256':hashlib.sha256(atlas_rgba).hexdigest(),
    'isoSha256':hashlib.sha256(iso_image).hexdigest(),
    'isoLength':len(iso_image),
    'isoVolumeIdentifier':'LSPTOOL_ORACLE',
    'isoFiles':{path:{'size':len(blob),'sha256':hashlib.sha256(blob).hexdigest()} for path,blob in sorted(iso_files.items())},
    'patchedIsoSha256':hashlib.sha256(patched_iso).hexdigest(),
    'patchedIsoLength':len(patched_iso),
    'patchedIsoVolumeBlocks':len(patched_iso)//ISO_SECTOR,
    'patchedIsoReplacements':patched_iso_replacements,
    'isoPatchManifestSha256':hashlib.sha256((json.dumps(iso_patch_manifest,indent=2)+'\n').encode('utf-8')).hexdigest(),
}
(FIX/'oracle.json').write_text(json.dumps(meta,indent=2),encoding='utf-8',newline='\n')
print(meta)
