'use client';

import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { AuthChangeEvent, Session, User, AuthError } from '@supabase/supabase-js';
import { UserProfile, UserRole } from '@/types';
import { getSupabaseBrowserClient } from '@/lib/supabase/client';

interface AuthContextType {
  user: UserProfile;
  role: UserRole;
  actualRole: UserRole;
  isLoading: boolean;
  isAdmin: boolean;
  isTeacher: boolean;
  isStudent: boolean;
  canAccessAdmin: boolean;
  canEditQuestions: boolean;
  canRunPipeline: boolean;
  roleDescription: string;
  signOut: () => Promise<void>;
  devRole?: UserRole | null;
  setDevRole?: (role: UserRole | null) => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

const emptyUser: UserProfile = {
  id: '',
  name: '',
  email: '',
  avatarUrl: '',
  role: 'STUDENT',
  streakDays: 0,
  targetScore: 0,
  totalXp: 0,
  isAuthenticated: false
};

function toRole(user: User | null): UserRole {
  // P0: Chỉ lấy quyền từ app_metadata do máy chủ quản lý, tuyệt đối không dùng user_metadata
  const value = user?.app_metadata?.role;
  if (typeof value !== 'string') return 'STUDENT';
  const role = value.toUpperCase();
  return role === 'ADMIN' || role === 'TEACHER' ? role : 'STUDENT';
}

function toProfile(user: User | null): UserProfile {
  if (!user) return emptyUser;
  return {
    ...emptyUser,
    id: user.id,
    name: user.user_metadata?.full_name || user.email || 'Học sinh',
    email: user.email || '',
    avatarUrl: user.user_metadata?.avatar_url || '',
    role: toRole(user),
    isAuthenticated: true
  };
}

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<UserProfile>(emptyUser);
  const [isLoading, setIsLoading] = useState(Boolean(process.env.NEXT_PUBLIC_SUPABASE_URL && process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY));
  const [devRoleOverride, setDevRoleOverride] = useState<UserRole | null>(() => {
    if (typeof window !== 'undefined' && process.env.NODE_ENV === 'development') {
      const saved = sessionStorage.getItem('omniprep_dev_role');
      if (saved === 'ADMIN' || saved === 'TEACHER' || saved === 'STUDENT') {
        return saved;
      }
    }
    return null;
  });

  const setDevRole = useCallback((newRole: UserRole | null) => {
    if (process.env.NODE_ENV !== 'development') return;
    setDevRoleOverride(newRole);
    if (newRole) {
      sessionStorage.setItem('omniprep_dev_role', newRole);
    } else {
      sessionStorage.removeItem('omniprep_dev_role');
    }
  }, []);

  useEffect(() => {
    let active = true;
    let supabase: ReturnType<typeof getSupabaseBrowserClient>;
    try {
      supabase = getSupabaseBrowserClient();
    } catch {
      return;
    }

    const { data: { subscription } } = supabase.auth.onAuthStateChange((_event: AuthChangeEvent, session: Session | null) => {
      if (active) setUser(toProfile(session?.user ?? null));
    });

    void supabase.auth.getUser().then((response: { data: { user: User | null }; error: AuthError | null }) => {
      if (active) {
        setUser(toProfile(response.error ? null : response.data.user));
        setIsLoading(false);
      }
    }).catch(() => {
      if (active) {
        setUser(emptyUser);
        setIsLoading(false);
      }
    });

    return () => {
      active = false;
      subscription.unsubscribe();
    };
  }, []);

  const signOut = useCallback(async () => {
    const supabase = getSupabaseBrowserClient();
    const { error } = await supabase.auth.signOut();
    if (error) throw error;
    setUser(emptyUser);
    if (process.env.NODE_ENV === 'development') {
      setDevRole(null);
    }
  }, [setDevRole]);

  const actualRole = user.role;
  const isDevOverrideActive = process.env.NODE_ENV === 'development' && devRoleOverride !== null;
  const role: UserRole = isDevOverrideActive ? (devRoleOverride as UserRole) : actualRole;

  const effectiveUser = useMemo<UserProfile>(() => {
    if (isDevOverrideActive) {
      return {
        ...user,
        name: user.isAuthenticated ? user.name : `Tài khoản thử nghiệm (${role})`,
        role,
        isAuthenticated: true
      };
    }
    return user;
  }, [user, isDevOverrideActive, role]);

  const isAdmin = role === 'ADMIN';
  const isTeacher = role === 'TEACHER';
  const isStudent = role === 'STUDENT';

  const value = useMemo<AuthContextType>(() => ({
    user: effectiveUser,
    role,
    actualRole,
    isLoading,
    isAdmin,
    isTeacher,
    isStudent,
    canAccessAdmin: isAdmin || isTeacher,
    canEditQuestions: isAdmin || isTeacher,
    canRunPipeline: isAdmin,
    roleDescription: isAdmin
      ? 'Quản trị viên'
      : isTeacher
        ? 'Giáo viên'
        : 'Học sinh',
    signOut,
    devRole: devRoleOverride,
    setDevRole
  }), [effectiveUser, role, actualRole, isLoading, isAdmin, isTeacher, isStudent, signOut, devRoleOverride, setDevRole]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within an AuthProvider');
  return context;
};

