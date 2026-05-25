use rand::RngCore;

/// Faithful port of `utils.packet_templates.ClientHelloMaker.get_client_hello_with`.
///
/// The original tool injects a precise 517-byte TLS ClientHello whose static
/// regions were hand-tuned to look like a real Chrome/Firefox hello so the DPI
/// middlebox classifies on this packet. Rebuilding it byte-for-byte matters:
/// firewalls that match on payload length or extension order will drop a
/// minimal/clean hello but accept this template.
///
/// Layout (517 bytes total, when SNI = "mci.ir" / 6 bytes):
///   [0..11)  static1         record + handshake + version
///   [11..43) random          32 bytes
///   [43..44) static2         session_id length (0x20)
///   [44..76) session_id      32 bytes
///   [76..120) static3        cipher_suites + compression + ext_len + ext header
///   [120..127+sni) sni_ext   server_name extension (dynamic on sni length)
///   [127+sni..262+sni) static4    further extensions (135 bytes)
///   [262+sni..294+sni) key_share  32 bytes
///   [294+sni..296+sni) static5    ext type 0x0015 (padding)
///   [296+sni..517) padding        len-prefixed zeros, fills to 517
pub fn build(target_sni: &[u8]) -> Vec<u8> {
    let mut rng = rand::thread_rng();
    let mut random = [0u8; 32];
    rng.fill_bytes(&mut random);
    let mut session_id = [0u8; 32];
    rng.fill_bytes(&mut session_id);
    let mut key_share = [0u8; 32];
    rng.fill_bytes(&mut key_share);

    build_from(&random, &session_id, target_sni, &key_share)
}

/// Lower-level: build with caller-supplied random / session / key_share.
/// Used in tests and any reproducibility paths.
pub fn build_from(random: &[u8; 32], session_id: &[u8; 32], target_sni: &[u8], key_share: &[u8; 32]) -> Vec<u8> {
    let sni_len = target_sni.len();
    let mut out = Vec::with_capacity(517 + sni_len.saturating_sub(6));

    out.extend_from_slice(&STATIC1);
    out.extend_from_slice(random);
    out.push(0x20);
    out.extend_from_slice(session_id);
    out.extend_from_slice(&STATIC3);

    // server_name extension:
    //   ext type   (handled in static3)... actually the type+ext-len bytes
    //   in the python live in static3, so here we emit only the inner block:
    //     list_len (2)  +  name_type (1)  +  name_len (2)  +  name
    let server_name_inner_len = (sni_len as u16) + 5;
    let name_list_len = (sni_len as u16) + 3;
    out.extend_from_slice(&server_name_inner_len.to_be_bytes());
    out.extend_from_slice(&name_list_len.to_be_bytes());
    out.push(0x00);
    out.extend_from_slice(&(sni_len as u16).to_be_bytes());
    out.extend_from_slice(target_sni);

    out.extend_from_slice(&STATIC4);
    out.extend_from_slice(key_share);
    out.extend_from_slice(&STATIC5);

    // padding extension: 2-byte big-endian payload length then zeros, total 221 - sni_len.
    let pad_payload = 219u16.saturating_sub(sni_len as u16);
    out.extend_from_slice(&pad_payload.to_be_bytes());
    out.extend(std::iter::repeat(0u8).take(pad_payload as usize));

    out
}

// Template bytes lifted directly from the Python source:
// tls_ch_template[:11]
const STATIC1: [u8; 11] = [
    0x16, 0x03, 0x01, 0x02, 0x00, 0x01, 0x00, 0x01, 0xfc, 0x03, 0x03,
];

// tls_ch_template[76:120]  — 44 bytes: cipher_suites + compression + ext block header
const STATIC3: [u8; 44] = [
    0x00, 0x24, 0x13, 0x02, 0x13, 0x03, 0x13, 0x01, 0xc0, 0x2c, 0xc0, 0x30, 0xc0, 0x2b, 0xc0, 0x2f,
    0xcc, 0xa9, 0xcc, 0xa8, 0xc0, 0x24, 0xc0, 0x28, 0xc0, 0x23, 0xc0, 0x27, 0x00, 0x9f, 0x00, 0x9e,
    0x00, 0x6b, 0x00, 0x67, 0x00, 0xff, 0x01, 0x00, 0x01, 0x8f, 0x00, 0x00,
];

// tls_ch_template[127 + 6 : 262 + 6]  -- 135 bytes
const STATIC4: [u8; 135] = [
    0x00, 0x0b, 0x00, 0x04, 0x03, 0x00, 0x01, 0x02, 0x00, 0x0a, 0x00, 0x16, 0x00, 0x14, 0x00, 0x1d,
    0x00, 0x17, 0x00, 0x1e, 0x00, 0x19, 0x00, 0x18, 0x01, 0x00, 0x01, 0x01, 0x01, 0x02, 0x01, 0x03,
    0x01, 0x04, 0x00, 0x23, 0x00, 0x00, 0x00, 0x10, 0x00, 0x0e, 0x00, 0x0c, 0x02, 0x68, 0x32, 0x08,
    0x68, 0x74, 0x74, 0x70, 0x2f, 0x31, 0x2e, 0x31, 0x00, 0x16, 0x00, 0x00, 0x00, 0x17, 0x00, 0x00,
    0x00, 0x0d, 0x00, 0x2a, 0x00, 0x28, 0x04, 0x03, 0x05, 0x03, 0x06, 0x03, 0x08, 0x07, 0x08, 0x08,
    0x08, 0x09, 0x08, 0x0a, 0x08, 0x0b, 0x08, 0x04, 0x08, 0x05, 0x08, 0x06, 0x04, 0x01, 0x05, 0x01,
    0x06, 0x01, 0x03, 0x03, 0x03, 0x01, 0x03, 0x02, 0x04, 0x02, 0x05, 0x02, 0x06, 0x02, 0x00, 0x2b,
    0x00, 0x05, 0x04, 0x03, 0x04, 0x03, 0x03, 0x00, 0x2d, 0x00, 0x02, 0x01, 0x01, 0x00, 0x33, 0x00,
    0x26, 0x00, 0x24, 0x00, 0x1d, 0x00, 0x20,
];

const STATIC5: [u8; 2] = [0x00, 0x15];

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn template_six_byte_sni_is_517_bytes() {
        let bytes = build_from(&[0u8; 32], &[0u8; 32], b"mci.ir", &[0u8; 32]);
        assert_eq!(bytes.len(), 517, "static template for 6-char SNI must be exactly 517 bytes");
        assert_eq!(bytes[0..3], [0x16, 0x03, 0x01]);
    }

    #[test]
    fn sni_appears_in_payload() {
        let sni = b"www.hcaptcha.com";
        let bytes = build_from(&[0u8; 32], &[0u8; 32], sni, &[0u8; 32]);
        assert!(bytes.windows(sni.len()).any(|w| w == sni));
    }
}
