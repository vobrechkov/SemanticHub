'use client';

/**
 * KeyboardShortcutsDialog - Dialog showing all available keyboard shortcuts
 */

import React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  Button,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Dismiss24Regular, Keyboard24Regular } from '@fluentui/react-icons';
import { KeyboardShortcut, formatShortcut } from '@/hooks/useKeyboardShortcuts';

export interface KeyboardShortcutsDialogProps {
  isOpen: boolean;
  onClose: () => void;
  shortcuts: KeyboardShortcut[];
}

const useStyles = makeStyles({
  dialogSurface: {
    maxWidth: '600px',
    minWidth: '500px',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: tokens.spacingVerticalL,
  },
  titleRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
  shortcutsTable: {
    width: '100%',
    borderCollapse: 'separate',
    borderSpacing: `0 ${tokens.spacingVerticalS}`,
  },
  shortcutRow: {
    marginBottom: tokens.spacingVerticalS,
  },
  shortcutKey: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    backgroundColor: tokens.colorNeutralBackground3,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    whiteSpace: 'nowrap',
    textAlign: 'right',
    paddingRight: tokens.spacingHorizontalL,
  },
  shortcutDescription: {
    paddingLeft: tokens.spacingHorizontalL,
    color: tokens.colorNeutralForeground2,
  },
  footer: {
    marginTop: tokens.spacingVerticalXL,
    paddingTop: tokens.spacingVerticalL,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    display: 'flex',
    justifyContent: 'center',
  },
});

export const KeyboardShortcutsDialog: React.FC<KeyboardShortcutsDialogProps> = ({
  isOpen,
  onClose,
  shortcuts,
}) => {
  const styles = useStyles();

  return (
    <Dialog open={isOpen} onOpenChange={(e, data) => !data.open && onClose()}>
      <DialogSurface className={styles.dialogSurface}>
        <DialogBody>
          <div className={styles.header}>
            <div className={styles.titleRow}>
              <Keyboard24Regular />
              <DialogTitle>Keyboard Shortcuts</DialogTitle>
            </div>
            <Button
              appearance="subtle"
              icon={<Dismiss24Regular />}
              onClick={onClose}
              aria-label="Close dialog"
            />
          </div>

          <DialogContent>
            <table className={styles.shortcutsTable}>
              <tbody>
                {shortcuts.map((shortcut, index) => (
                  <tr key={index} className={styles.shortcutRow}>
                    <td className={styles.shortcutKey}>
                      <Text>{formatShortcut(shortcut)}</Text>
                    </td>
                    <td className={styles.shortcutDescription}>
                      <Text>{shortcut.description}</Text>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            <div className={styles.footer}>
              <Button appearance="primary" onClick={onClose}>
                Got it
              </Button>
            </div>
          </DialogContent>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

export default KeyboardShortcutsDialog;
