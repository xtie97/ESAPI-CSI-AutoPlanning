"""
room_summary.py - read approved RT Ion Plan exports (RN*.dcm) and suggest the room entries
(new MachineOption { ... }) of SiteSettings.cs for every treatment room found.

Usage (Python 3 + pydicom):
    pip install pydicom numpy
    python room_summary.py <file or folder> [<file or folder> ...]

Only technical plan content is printed (machine, tolerance table, snout, range shifter, patient support,
field geometry). Patient identifiers are never printed; files that are not marked as de-identified are
reported by file name only so you can check them.

What it reads (DICOM tag -> MachineOption field in SiteSettings.cs):
    Treatment Machine Name          (300A,00B2) -> MachineId
    Tolerance Table Label           (300A,0043) -> ToleranceId
    Snout ID                        (300A,030F) -> SnoutId
    Range Shifter ID                (300A,0318) -> RangeShifterId
    Patient Support ID              (300A,0352) -> PatientSupportDeviceId
    Snout Position                  (300A,030D) minus
    Isocenter to Range Shifter Dist (300A,0364) -> SnoutToRangeShifterMm
    Scan Spot Position Map          (300A,0394) -> farthest spot from iso (checks HalfFieldLimitMm)
    gantry angles / isocenters / spot extents  -> SpineScheme (PostBrain / PostExtended / Separate)
"""
import os
import sys
from collections import Counter, defaultdict

import numpy as np
import pydicom


def files(args):
    for a in args:
        if os.path.isdir(a):
            for root, _, names in os.walk(a):
                for n in sorted(names):
                    if n.lower().endswith('.dcm'):
                        yield os.path.join(root, n)
        else:
            yield a


def spot_y_range(beam):
    ys = []
    for cp in beam.IonControlPointSequence:
        m = getattr(cp, 'ScanSpotPositionMap', None)
        if m is None:
            continue
        a = np.array(list(map(float, m))).reshape(-1, 2)
        ys.extend(a[:, 1])
    return (min(ys), max(ys)) if ys else (0.0, 0.0)


def scheme(tx):
    """Classify the field arrangement of one plan."""
    lat = [b for b in tx if abs(b['gantry'] - 180) > 30]
    if not lat:
        return 'unknown'
    brain_z = lat[0]['iso'][2]
    post_brain = [b for b in tx if abs(b['gantry'] - 180) < 1 and abs(b['iso'][2] - brain_z) < 1]
    if not post_brain:
        return 'Separate'
    lat_low = min(b['iso'][2] + b['ymin'] for b in lat)
    post_low = min(b['iso'][2] + b['ymin'] for b in post_brain)
    return 'PostExtended' if post_low < lat_low - 30 else 'PostBrain'


def main(argv):
    rooms = defaultdict(lambda: defaultdict(Counter))
    plans_per_room = Counter()
    not_deid = []
    for path in files(argv):
        try:
            ds = pydicom.dcmread(path, force=True)
        except Exception as ex:  # not DICOM
            print(f"skip {os.path.basename(path)}: {ex}")
            continue
        if str(getattr(ds, 'Modality', '')) != 'RTPLAN' or 'IonBeamSequence' not in ds:
            continue
        if str(getattr(ds, 'PatientIdentityRemoved', '')).upper() != 'YES':
            not_deid.append(os.path.basename(path))
        tol = {str(t.ToleranceTableNumber): str(t.ToleranceTableLabel) for t in getattr(ds, 'IonToleranceTableSequence', [])}
        tx = []
        for b in ds.IonBeamSequence:
            if str(getattr(b, 'TreatmentDeliveryType', '')) != 'TREATMENT':
                continue
            cp0 = b.IonControlPointSequence[0]
            rs = cp0.RangeShifterSettingsSequence[0] if getattr(cp0, 'RangeShifterSettingsSequence', None) else None
            y0, y1 = spot_y_range(b)
            tx.append(dict(
                machine=str(b.TreatmentMachineName),
                tol=tol.get(str(getattr(b, 'ReferencedToleranceTableNumber', '')), ''),
                snout=str(b.SnoutSequence[0].SnoutID) if getattr(b, 'SnoutSequence', None) else '',
                rs=str(b.RangeShifterSequence[0].RangeShifterID) if getattr(b, 'RangeShifterSequence', None) else '',
                psd=str(getattr(b, 'PatientSupportID', '')),
                offset=round(float(cp0.SnoutPosition) - float(rs.IsocenterToRangeShifterDistance), 1) if rs is not None and getattr(cp0, 'SnoutPosition', None) is not None else None,
                gantry=float(cp0.GantryAngle), couch=float(cp0.PatientSupportAngle),
                iso=np.array(list(map(float, cp0.IsocenterPosition))), ymin=y0, ymax=y1,
                snoutpos=float(cp0.SnoutPosition) if getattr(cp0, 'SnoutPosition', None) is not None else None))
        if not tx:
            continue
        room = tx[0]['machine']
        plans_per_room[room] += 1
        r = rooms[room]
        r['scheme'][scheme(tx)] += 1
        r['position'].update(str(p.PatientPosition) for p in ds.PatientSetupSequence)
        for b in tx:
            for k in ('tol', 'snout', 'rs', 'psd', 'offset'):
                r[k][b[k]] += 1
            r['couch'][b['couch']] += 1
            r['maxspot'][max(abs(b['ymin']), abs(b['ymax']))] += 1
            if b['snoutpos'] is not None:
                r['snoutpos'][round(b['snoutpos'] / 10)] += 1

    if not_deid:
        print("NOTE: not marked as de-identified (check before sharing): " + ", ".join(not_deid))
    for room in sorted(rooms):
        r = rooms[room]
        top = lambda k: r[k].most_common(1)[0][0] if r[k] else ''
        maxspot = max(r['maxspot']) if r['maxspot'] else 0
        # commissioned sup-inf scanning limits (half field) of the IBA rooms; other rooms: round the clinical maximum up
        known = {'Proton_1': 170, 'Proton_3': 170, 'Proton_4': 200, 'Proton_5': 200}
        limit = known.get(room, int(np.ceil(maxspot / 10.0) * 10))
        print("=" * 100)
        print(f"{room}: {plans_per_room[room]} plan(s)")
        for k, label in (('tol', 'tolerance'), ('snout', 'snout'), ('rs', 'range shifter'), ('psd', 'patient support'),
                         ('offset', 'snout - iso-to-RS (mm)'), ('scheme', 'field arrangement'), ('position', 'patient position'),
                         ('couch', 'couch angle')):
            mc = r[k].most_common()
            tie = len(mc) > 1 and mc[0][1] == mc[1][1]
            print(f"  {label:24s} " + ", ".join(f"{v!s} x{n}" for v, n in mc) + ("   <- tie: choose one" if tie else ""))
        print(f"  {'farthest spot from iso':24s} {maxspot / 10:.1f} cm   (HalfFieldLimitMm must be at least this)")
        print(f"  {'snout positions (cm)':24s} {min(r['snoutpos'])}-{max(r['snoutpos'])}" if r['snoutpos'] else "")
        disp = room.replace('_', ' ') + " (IBA)"
        off = top('offset')
        print("  suggested line for SiteSettings.cs, Machines (check ties above, HalfFieldLimitMm and InitialSnoutPosition):")
        if off is not None:
            print(f'    new MachineOption {{ DisplayName = "{disp}", MachineId = "{room}", Enabled = true, TechniqueId = "MODULAT_SCANNING", '
                  f'ToleranceId = "{top("tol")}", SnoutId = "{top("snout")}", RangeShifterId = "{top("rs")}", '
                  f'PatientSupportDeviceId = "{top("psd")}", HalfFieldLimitMm = {limit}, InitialSnoutPosition = 30, '
                  f'SpineScheme = SpineSchemes.{top("scheme")}, SnoutToRangeShifterMm = {off:g}, SnoutSizeXcm = 37, SnoutSizeYcm = 45 }},')


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1:])
